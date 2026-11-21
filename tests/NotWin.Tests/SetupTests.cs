using System.IO;
using System.IO.Compression;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using NotWin.Setup;

namespace NotWin.Tests;

// One-click setup (2026-11-21): the app rides at the end of NotWin-Setup.exe, and each PC signs it with its own
// certificate whose private key only ever lives in memory.
public sealed class SetupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nw-setup-" + Guid.NewGuid().ToString("N"));

    public SetupTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Write(string name, byte[] bytes)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    [Fact]
    public void Payload_RoundTrip_FindsTheZipAndStripsItForTheUninstaller()
    {
        var stub = Write("stub.exe", [1, 2, 3, 4, 5]);
        var zip = Write("payload.zip", [9, 8, 7]);
        var setup = Path.Combine(_dir, "setup.exe");
        Payload.Append(stub, zip, setup);

        var found = Payload.Find(setup);
        Assert.NotNull(found);
        Assert.Equal((5L, 3L), found!.Value);

        var copy = Path.Combine(_dir, "payload-out.zip");
        Payload.Extract(setup, found.Value, copy);
        Assert.Equal([9, 8, 7], File.ReadAllBytes(copy));

        var uninstaller = Path.Combine(_dir, "uninstall.exe");
        Payload.WriteStub(setup, uninstaller);
        Assert.Equal([1, 2, 3, 4, 5], File.ReadAllBytes(uninstaller));
        Assert.Null(Payload.Find(uninstaller));
    }

    [Fact]
    public void Payload_PlainExe_HasNone()
    {
        Assert.Null(Payload.Find(Write("plain.exe", [1, 2, 3])));
        Assert.Null(Payload.Find(Write("long.exe", new byte[64])));
    }

    [Fact]
    public void Certificate_IsACodeSigningLeafWithAnInMemoryKey()
    {
        using var cert = PerPcSigner.CreateCertificate();
        Assert.Equal("CN=NotWin Dev", cert.Subject); // must equal the package Publisher
        Assert.True(cert.HasPrivateKey);
        var eku = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();
        Assert.Equal(["1.3.6.1.5.5.7.3.3"], eku.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Select(o => o.Value!));
        Assert.False(cert.Extensions.OfType<X509BasicConstraintsExtension>().Single().CertificateAuthority);
        Assert.True(cert.NotAfter > DateTime.Now.AddYears(20));
    }

    [Fact]
    public void Sign_APortableExecutable_CarriesThatCertificate()
    {
        var pe = Path.Combine(_dir, "NotWin.Setup.dll");
        File.Copy(typeof(Payload).Assembly.Location, pe);
        using var cert = PerPcSigner.CreateCertificate();
        PerPcSigner.Sign(pe, cert);
#pragma warning disable SYSLIB0057 // reading the Authenticode signer of a signed file
        using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(pe));
#pragma warning restore SYSLIB0057
        Assert.Equal(cert.Thumbprint, signer.Thumbprint);
    }

    [Fact]
    public void Sign_AnMsix_CarriesThatCertificate()
    {
        var makeappx = FindMakeAppx();
        Assert.True(makeappx is not null, "makeappx.exe not in the NuGet cache: run build\\build.ps1 once");
        var content = Path.Combine(_dir, "pkg");
        Directory.CreateDirectory(Path.Combine(content, "Assets"));
        var repo = RepoRoot();
        File.Copy(Path.Combine(repo, @"src\NotWin\Package\AppxManifest.xml"), Path.Combine(content, "AppxManifest.xml"));
        File.Copy(Path.Combine(repo, @"src\NotWin\Package\Assets\logo.png"), Path.Combine(content, @"Assets\logo.png"));
        var msix = Path.Combine(_dir, "NotWin.msix");
        using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(makeappx!, $"pack /nv /o /d \"{content}\" /p \"{msix}\"")
               { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })!)
        {
            p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Assert.Equal(0, p.ExitCode);
        }

        using var cert = PerPcSigner.CreateCertificate();
        PerPcSigner.Sign(msix, cert);

        using var zip = ZipFile.OpenRead(msix);
        var entry = zip.GetEntry("AppxSignature.p7x");
        Assert.NotNull(entry);
        using var ms = new MemoryStream();
        using (var s = entry!.Open()) s.CopyTo(ms);
        var p7x = ms.ToArray();
        Assert.Equal("PKCX"u8.ToArray(), p7x[..4]);
        var cms = new SignedCms();
        cms.Decode(p7x[4..]);
        Assert.Equal(cert.Thumbprint, cms.SignerInfos[0].Certificate!.Thumbprint);
    }

    [Fact]
    public void FilesToSign_AreOnlyNotWinsOwnBinaries()
    {
        var app = Path.Combine(_dir, "app");
        Directory.CreateDirectory(Path.Combine(app, "runtimes"));
        foreach (var n in new[] { "NotWin.exe", "NotWin.dll", "NotWin.Core.dll", "NotWin.Watchdog.exe", "NotWin.DragHelper.exe",
                                  "coreclr.dll", "System.Private.CoreLib.dll", "NotWin.pdb", @"runtimes\NotWin.Fake.dll" })
            File.WriteAllBytes(Path.Combine(app, n), [0]);
        var names = PerPcSigner.FilesToSign(app).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["NotWin.Core.dll", "NotWin.DragHelper.exe", "NotWin.Watchdog.exe", "NotWin.dll", "NotWin.exe"], names);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "NotWin.sln"))) d = d.Parent;
        return d!.FullName;
    }

    private static string? FindMakeAppx()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".nuget\packages\microsoft.windows.sdk.buildtools");
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateFiles(root, "makeappx.exe", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == "x64").OrderDescending().FirstOrDefault();
    }
}
