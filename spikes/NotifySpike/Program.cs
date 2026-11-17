// THROWAWAY spike: UserNotificationListener from a sparse-package-identity exe.
// Usage: NotifySpike.exe [--seconds N]  (default 60). Log: %TEMP%\NotifySpike.log (also stdout).
using System.Runtime.InteropServices;
using System.Text;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

static class P
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref int len, StringBuilder? name);

    static readonly string LogPath = Path.Combine(Path.GetTempPath(), "NotifySpike.log");
    static void Log(string s)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {s}";
        Console.WriteLine(line);
        try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch { }
    }

    static string Describe(UserNotification n)
    {
        string app = "?";
        try { app = n.AppInfo.DisplayInfo.DisplayName + " [" + n.AppInfo.AppUserModelId + "]"; } catch (Exception e) { app = "ERR " + e.GetType().Name + " " + e.Message; }
        var texts = new List<string>();
        try
        {
            var b = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (b != null) foreach (var t in b.GetTextElements()) texts.Add(t.Text);
        }
        catch (Exception e) { texts.Add("ERR " + e.Message); }
        return $"id={n.Id} app={app} text=\"{string.Join(" | ", texts)}\"";
    }

    static async Task<int> Main(string[] args)
    {
        int seconds = 60;
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--seconds") seconds = int.Parse(args[i + 1]);
        File.WriteAllText(LogPath, "");
        Log($"exe={Environment.ProcessPath} pid={Environment.ProcessId} uiAccessManifest={(Environment.ProcessPath ?? "").Contains("Program Files")}");
        int len = 0;
        int rc = GetCurrentPackageFullName(ref len, null);
        if (rc == 15700) Log("GetCurrentPackageFullName: APPMODEL_ERROR_NO_PACKAGE (15700) -> NO package identity");
        else
        {
            var sb = new StringBuilder(len);
            rc = GetCurrentPackageFullName(ref len, sb);
            Log($"GetCurrentPackageFullName rc={rc} name={sb}");
        }

        UserNotificationListener l;
        try { l = UserNotificationListener.Current; } catch (Exception e) { Log("Current threw " + e); return 2; }
        try { Log("GetAccessStatus (before) = " + l.GetAccessStatus()); } catch (Exception e) { Log("GetAccessStatus threw " + e.GetType().Name + ": " + e.Message); }
        UserNotificationListenerAccessStatus st;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            st = await l.RequestAccessAsync();
            Log($"RequestAccessAsync returned {st} after {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception e) { Log("RequestAccessAsync threw " + e.GetType().Name + " 0x" + e.HResult.ToString("X") + ": " + e.Message); return 3; }
        Log("GetAccessStatus (after) = " + l.GetAccessStatus());
        if (st != UserNotificationListenerAccessStatus.Allowed) { Log("Not Allowed -> stopping"); return 4; }

        var seen = new HashSet<uint>();
        try
        {
            var list = await l.GetNotificationsAsync(NotificationKinds.Toast);
            Log($"GetNotificationsAsync(Toast) count={list.Count}");
            foreach (var n in list) { seen.Add(n.Id); Log("  EXISTING " + Describe(n)); }
            if (args.Contains("--cleanup"))
            {
                int removed = 0;
                foreach (var n in list) if (Describe(n).Contains("NothingShell spike test")) { l.RemoveNotification(n.Id); removed++; }
                Log($"cleanup: removed {removed} test notifications");
                return 0;
            }
        }
        catch (Exception e) { Log("GetNotificationsAsync threw " + e.GetType().Name + ": " + e.Message); }

        try
        {
            l.NotificationChanged += (s, a) =>
            {
                try
                {
                    var n = s.GetNotification(a.UserNotificationId);
                    Log($"EVENT NotificationChanged kind={a.ChangeKind} id={a.UserNotificationId} " + (n != null ? Describe(n) : "(notification null)"));
                }
                catch (Exception e) { Log("EVENT handler threw " + e.Message); }
            };
            Log("NotificationChanged handler attached");
        }
        catch (Exception e) { Log("NotificationChanged attach threw " + e.GetType().Name + ": " + e.Message); }

        Log($"Listening {seconds}s (polling GetNotificationsAsync every 1s for ids not in the initial set)...");
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            await Task.Delay(1000);
            try
            {
                var list = await l.GetNotificationsAsync(NotificationKinds.Toast);
                foreach (var n in list) if (seen.Add(n.Id)) Log("  POLL NEW " + Describe(n));
            }
            catch (Exception e) { Log("poll threw " + e.Message); }
        }
        Log("done");
        return 0;
    }
}
