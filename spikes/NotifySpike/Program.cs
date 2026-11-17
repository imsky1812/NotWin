// THROWAWAY spike: UserNotificationListener from a sparse-package-identity exe.
// Usage: NotifySpike.exe [--seconds N] [--expect "<text>"] [--verbose] [--cleanup]  (default 60 s). Log: %TEMP%\NotifySpike.log (also stdout).
// By default only test toasts ("NothingShell spike test") are printed in full; other apps' notifications are redacted.
// With --verbose the log contains OTHER APPS' NOTIFICATION TEXT: never commit or share it.
using System.Runtime.InteropServices;
using System.Text;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

static class P
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref int len, StringBuilder? name);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr proc, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(IntPtr token, int cls, out uint info, int len, out int ret);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    static string TokenUiAccess()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x8 /*TOKEN_QUERY*/, out var tok)) return "OpenProcessToken failed " + Marshal.GetLastWin32Error();
        try { return GetTokenInformation(tok, 26 /*TokenUIAccess*/, out var v, 4, out _) ? v.ToString() : "GetTokenInformation failed " + Marshal.GetLastWin32Error(); }
        finally { CloseHandle(tok); }
    }

    static bool verbose;
    static string? expect;
    static readonly string LogPath = Path.Combine(Path.GetTempPath(), "NotifySpike.log");
    static void Log(string s)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {s}";
        Console.WriteLine(line);
        try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch { }
    }

    static string Describe(UserNotification n)
    {
        var full = DescribeFull(n);
        if (verbose || full.Contains("NothingShell spike test")) return full;
        return $"id={n.Id} (non-test notification, content redacted)";
    }

    static void Check(string described)
    {
        if (expect == null || !described.Contains("NothingShell spike test")) return;
        Log(described.Contains("text=\"" + expect) ? $"EXPECT MATCH (text starts with \"{expect}\")" : $"EXPECT MISMATCH (wanted text starting with \"{expect}\")");
    }

    static string DescribeFull(UserNotification n)
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
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--expect") expect = args[i + 1];
        verbose = args.Contains("--verbose");
        File.WriteAllText(LogPath, "");
        Log($"exe={Environment.ProcessPath} pid={Environment.ProcessId} inProgramFiles={(Environment.ProcessPath ?? "").Contains("Program Files", StringComparison.OrdinalIgnoreCase)} tokenUIAccess={TokenUiAccess()} verbose={verbose}");
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
                foreach (var n in list) if (DescribeFull(n).Contains("NothingShell spike test")) { l.RemoveNotification(n.Id); removed++; }
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
                    if (n != null) Check(Describe(n));
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
                foreach (var n in list) if (seen.Add(n.Id)) { Log("  POLL NEW " + Describe(n)); Check(Describe(n)); }
            }
            catch (Exception e) { Log("poll threw " + e.Message); }
        }
        Log("done");
        return 0;
    }
}
