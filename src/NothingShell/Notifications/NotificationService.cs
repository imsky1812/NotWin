using NothingShell.Core.Panel;
using NothingShell.Native;
using NothingShell.Shell;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NothingShell.Notifications;

/// <summary>
/// Windows' notifications through UserNotificationListener (needs the sparse package identity, Task 2; proven by the
/// spike, hands-on N.1-N.4). Events are delivered on the UI context <see cref="StartAsync"/> ran on.
/// Privacy: notification text stays in memory; only app names, counts and ids are ever logged.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private UserNotificationListener? _listener;
    private readonly List<NotificationItem> _local = [];
    private uint _nextLocalId = uint.MaxValue;
    private SynchronizationContext? _ui;
    private bool _disposed;

    public bool Available { get; private set; }

    /// <summary>Why notifications are unavailable: "no package identity", "access denied" or "listener error".</summary>
    public string? UnavailableReason { get; private set; }

    /// <summary>A new notification arrived (UI thread).</summary>
    public event Action<NotificationItem>? Added;

    /// <summary>Something was added or removed (UI thread): the panel re-reads the list.</summary>
    public event Action? ListChanged;

    public async Task StartAsync()
    {
        _ui = SynchronizationContext.Current;
        if (!PackageIdentity.HasIdentity(out _))
        {
            UnavailableReason = "no package identity";
            Log.Info("notifications unavailable: no package identity (run build\\install.ps1)");
            return;
        }
        try
        {
            _listener = UserNotificationListener.Current;
            var access = await _listener.RequestAccessAsync();
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                UnavailableReason = "access denied";
                Log.Info($"notifications unavailable: access {access}");
                return;
            }
            _listener.NotificationChanged += OnChanged;
            Available = true;
            Log.Info("notifications: listener ready");
        }
        catch (Exception e)
        {
            UnavailableReason = "listener error";
            Log.Error("notification listener failed", e);
        }
    }

    /// <summary>
    /// Adds Nothing Shell's own notification (e.g. the timer's "Time is up") to the panel list. Local items have ids
    /// counting down from uint.MaxValue and an empty AppId; they live in memory only and are listed even without the
    /// Windows listener.
    /// </summary>
    public NotificationItem AddLocal(string appName, string text)
    {
        var item = new NotificationItem(_nextLocalId--, "", appName, text, DateTimeOffset.Now);
        _local.Add(item);
        Post(() => ListChanged?.Invoke());
        return item;
    }

    public async Task<IReadOnlyList<NotificationItem>> GetAllAsync()
    {
        var local = _local.ToList();
        if (!Available || _listener is null) return local;
        return local.Concat(await GetWindowsAsync()).ToList();
    }

    private async Task<IReadOnlyList<NotificationItem>> GetWindowsAsync()
    {
        if (_listener is null) return [];
        try
        {
            var list = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            return list.Select(ToItem).Where(i => i is not null).Select(i => i!).ToList();
        }
        catch (Exception e)
        {
            Log.Error("reading notifications failed", e);
            return [];
        }
    }

    public void Remove(IEnumerable<uint> ids)
    {
        foreach (var id in ids)
        {
            if (_local.RemoveAll(i => i.Id == id) > 0 || _listener is null) continue;
            try { _listener.RemoveNotification(id); }
            catch (Exception e) { Log.Error($"removing notification {id} failed", e); }
        }
        Post(() => ListChanged?.Invoke());
    }

    public void ClearAll()
    {
        _local.Clear();
        if (_listener is null) { Post(() => ListChanged?.Invoke()); return; }
        try { _listener.ClearNotifications(); }
        catch (Exception e) { Log.Error("clearing notifications failed", e); }
        Post(() => ListChanged?.Invoke());
    }

    /// <summary>Opens the notification's app (its AppUserModelId through shell:AppsFolder).</summary>
    public bool OpenApp(string appId)
    {
        var ok = !string.IsNullOrWhiteSpace(appId) && ShellLinkNative.Open(@"shell:AppsFolder\" + appId);
        Log.Info($"notification open app ok={ok}");
        return ok;
    }

    private void OnChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (_disposed) return;
        NotificationItem? item = null;
        if (args.ChangeKind == UserNotificationChangedKind.Added)
        {
            try { item = sender.GetNotification(args.UserNotificationId) is { } n ? ToItem(n) : null; }
            catch (Exception e) { Log.Error("reading a new notification failed", e); }
            if (item is not null) Log.Info($"notification added app={item.AppName} id={item.Id}");
        }
        Post(() =>
        {
            if (item is not null) Added?.Invoke(item);
            ListChanged?.Invoke();
        });
    }

    private static NotificationItem? ToItem(UserNotification n)
    {
        try
        {
            string appId = "", appName = "";
            try
            {
                appId = n.AppInfo.AppUserModelId ?? "";
                appName = n.AppInfo.DisplayInfo.DisplayName ?? "";
            }
            catch { /* some system toasts have no app info */ }
            var texts = new List<string>();
            var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding is not null)
                foreach (var t in binding.GetTextElements())
                    if (!string.IsNullOrWhiteSpace(t.Text)) texts.Add(t.Text.Trim());
            return new NotificationItem(n.Id, appId, string.IsNullOrEmpty(appName) ? "Notification" : appName,
                string.Join(" · ", texts), n.CreationTime);
        }
        catch { return null; }
    }

    private void Post(Action a)
    {
        if (_ui is null) a();
        else _ui.Post(_ => { try { a(); } catch (Exception e) { Log.Error("notification handler failed", e); } }, null);
    }

    public void Dispose()
    {
        _disposed = true;
        if (_listener is not null && Available)
        {
            try { _listener.NotificationChanged -= OnChanged; } catch { }
        }
    }
}
