using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace NotWin.Setup;

/// <summary>
/// What setup does on the PC, step by step (runs elevated). Mirrors build\install.ps1, minus the build: the payload is
/// signed here with a per-PC certificate (see <see cref="PerPcSigner"/>).
/// </summary>
public static class Installer
{
    public static string InstallDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NotWin");
    private static string StageDir => InstallDir + ".staging"; // under Program Files: only admins can write there
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NotWin";
    private const string StateKey = @"SOFTWARE\NotWin";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string Version = "1.5.0.0";

    public static void Install(string setupExe, (long Offset, long Length) payload, Action<string> log)
    {
        // 1. Unpack into the admin-only staging folder.
        if (Directory.Exists(StageDir)) Directory.Delete(StageDir, true);
        Directory.CreateDirectory(StageDir);
        var zip = Path.Combine(StageDir, "payload.zip");
        Payload.Extract(setupExe, payload, zip);
        ZipFile.ExtractToDirectory(zip, StageDir);
        File.Delete(zip);
        var app = Path.Combine(StageDir, "app");
        var msix = Path.Combine(StageDir, "NotWin.msix");
        if (!File.Exists(Path.Combine(app, "NotWin.exe")) || !File.Exists(msix)) throw new InvalidDataException("the setup file is damaged");
        log("unpacked");

        // 2. Sign with a certificate made for this PC, then trust only its public part.
        using var cert = PerPcSigner.CreateCertificate();
        var files = PerPcSigner.FilesToSign(app).ToList();
        foreach (var f in files) PerPcSigner.Sign(f, cert);
        PerPcSigner.Sign(msix, cert);
        log($"signed {files.Count} files and the package");
        Trust(cert);
        log("certificate trusted on this PC");

        // 3. Stop a running copy and give the taskbar back (with the new, signed watchdog).
        StopRunning(Path.Combine(app, "NotWin.Watchdog.exe"), log);

        // 4. Replace the files.
        if (Directory.Exists(InstallDir)) DeleteWithRetry(InstallDir);
        Directory.Move(app, InstallDir);
        Payload.WriteStub(setupExe, Path.Combine(InstallDir, "uninstall.exe"));
        log("installed");

        // 5. Package identity (notifications). Optional: without it NotWin runs without notification access.
        var ps = PowerShell($"Get-AppxPackage -Name NotWin | Remove-AppxPackage; Add-AppxPackage -Path '{msix}' -ExternalLocation '{InstallDir}'");
        log(ps == 0 ? "package registered" : $"package registration failed (exit {ps}); notifications stay off");

        // 6. Forget the certificate of an earlier setup run, and add the Settings > Apps entry.
        using (var state = Registry.LocalMachine.CreateSubKey(StateKey))
        {
            if (state.GetValue("Certificate") is string old && !old.Equals(cert.Thumbprint, StringComparison.OrdinalIgnoreCase)) Untrust(old);
            state.SetValue("Certificate", cert.Thumbprint);
        }
        using (var u = Registry.LocalMachine.CreateSubKey(UninstallKey))
        {
            u.SetValue("DisplayName", "NotWin");
            u.SetValue("DisplayVersion", Version);
            u.SetValue("Publisher", "NotWin");
            u.SetValue("DisplayIcon", Path.Combine(InstallDir, "NotWin.exe"));
            u.SetValue("InstallLocation", InstallDir);
            u.SetValue("UninstallString", $"\"{Path.Combine(InstallDir, "uninstall.exe")}\" --uninstall");
            u.SetValue("NoModify", 1, RegistryValueKind.DWord);
            u.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            u.SetValue("EstimatedSize", (int)(DirectorySize(InstallDir) / 1024), RegistryValueKind.DWord);
        }
        TryDelete(StageDir);

        // 7. Start NotWin as the signed-in user, not as admin (explorer.exe hands it to the user's own Explorer).
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.Combine(InstallDir, "NotWin.exe")}\"") { UseShellExecute = false })?.Dispose();
        log("started");
    }

    public static void Uninstall(Action<string> log)
    {
        StopRunning(Path.Combine(InstallDir, "NotWin.Watchdog.exe"), log);
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)) run?.DeleteValue("NotWin", throwOnMissingValue: false);
        PowerShell("Get-AppxPackage -Name NotWin | Remove-AppxPackage");
        using (var state = Registry.LocalMachine.OpenSubKey(StateKey))
            if (state?.GetValue("Certificate") is string thumb) Untrust(thumb);
        Registry.LocalMachine.DeleteSubKeyTree(StateKey, throwOnMissingSubKey: false);
        Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        log("removed the start entry, package, certificate and Apps entry");

        // Everything but this running uninstaller, which a short cmd removes with the folder once it has exited.
        var self = Environment.ProcessPath ?? "";
        foreach (var f in Directory.Exists(InstallDir) ? Directory.EnumerateFiles(InstallDir, "*", SearchOption.AllDirectories).ToList() : [])
            if (!f.Equals(self, StringComparison.OrdinalIgnoreCase)) TryDeleteFile(f);
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            $"/d /c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{InstallDir}\"") { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
        log("files removed");
    }

    private static void Trust(X509Certificate2 cert)
    {
        using var publicOnly = X509CertificateLoader.LoadCertificate(cert.RawData);
        foreach (var name in new[] { StoreName.Root, StoreName.TrustedPublisher })
        {
            using var store = new X509Store(name, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            store.Add(publicOnly);
        }
    }

    private static void Untrust(string thumbprint)
    {
        foreach (var name in new[] { StoreName.Root, StoreName.TrustedPublisher })
        {
            using var store = new X509Store(name, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            foreach (var c in store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)) store.Remove(c);
        }
    }

    // As install.ps1: only the shell is killed, its watchdog then restores the taskbar by itself; if the watchdog is
    // stuck or was never running, the given (signed) watchdog restores it. A failed restore stops setup before any file
    // is replaced, so the taskbar can always be given back.
    private static void StopRunning(string watchdog, Action<string> log)
    {
        var watchdogWasRunning = Running("NotWin.Watchdog");
        var shellWasRunning = Running("NotWin");
        Kill("NotWin");
        var deadline = Environment.TickCount64 + 10_000;
        while (Running("NotWin.Watchdog") && Environment.TickCount64 < deadline) Thread.Sleep(250);
        if ((shellWasRunning && !watchdogWasRunning) || Running("NotWin.Watchdog") || HasTaskbarState())
        {
            if (File.Exists(watchdog))
            {
                var state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotWin");
                using var p = Process.Start(new ProcessStartInfo(watchdog) { ArgumentList = { "--restore-now", "--state", state }, UseShellExecute = false })!;
                p.WaitForExit();
                if (p.ExitCode != 0) throw new InvalidOperationException($"giving the taskbar back failed (exit {p.ExitCode}); nothing was changed");
            }
            Kill("NotWin.Watchdog");
        }
        Kill("NotWin.DragHelper");
        if (shellWasRunning) log("stopped the running NotWin");
        Thread.Sleep(500);
    }

    private static bool HasTaskbarState() =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotWin", "taskbar-restore.json"));

    private static bool Running(string name)
    {
        var ps = Process.GetProcessesByName(name);
        foreach (var p in ps) p.Dispose();
        return ps.Length > 0;
    }

    private static void Kill(string name)
    {
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { p.Kill(); p.WaitForExit(10_000); } catch (Exception) { /* already gone */ }
            p.Dispose();
        }
    }

    private static int PowerShell(string command)
    {
        var exe = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        using var p = Process.Start(new ProcessStartInfo(exe)
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command + "; exit 0" },
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        p.StandardOutput.ReadToEnd();
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode != 0 ? p.ExitCode : err.Length > 0 ? 1 : 0;
    }

    private static void DeleteWithRetry(string dir)
    {
        for (var i = 0; ; i++)
        {
            try { Directory.Delete(dir, true); return; }
            catch (Exception) when (i < 20) { Thread.Sleep(500); }
        }
    }

    private static long DirectorySize(string dir) => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
    private static void TryDelete(string dir) { try { Directory.Delete(dir, true); } catch (Exception) { } }
    private static void TryDeleteFile(string f) { try { File.Delete(f); } catch (Exception) { } }
}
