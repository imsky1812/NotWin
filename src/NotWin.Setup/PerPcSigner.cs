using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NotWin.Setup;

/// <summary>
/// Signs NotWin on the PC it is installed on. Windows only gives uiAccess (the dock above every window) to a signed
/// exe whose certificate it trusts, and only registers a signed package. Instead of shipping one certificate that
/// every PC would have to trust, setup makes a new one per PC. Its private key is an in-memory CNG key that is never
/// written anywhere: once setup exits nothing else can ever be signed with it, and only the public part is trusted.
/// </summary>
public static unsafe class PerPcSigner
{
    /// <summary>Must equal the Publisher in Package/AppxManifest.xml and the msix element of app.uiaccess.manifest.</summary>
    public const string Subject = "CN=NotWin Dev";

    public static X509Certificate2 CreateCertificate()
    {
        using var key = new RSACng(3072); // ephemeral: lives only in this process
        var request = new CertificateRequest(Subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.3")], false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        // Signatures carry no timestamp (setup works offline), so they are valid while the certificate is.
        return request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(30));
    }

    /// <summary>NotWin's own exe and dll files in the app folder (the .NET runtime files are already signed by Microsoft).</summary>
    public static IEnumerable<string> FilesToSign(string appDir) =>
        Directory.EnumerateFiles(appDir, "NotWin*", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

    /// <summary>Authenticode-signs a PE file or an .msix with <paramref name="cert"/> (SHA-256), like signtool does.</summary>
    public static void Sign(string path, X509Certificate2 cert)
    {
        var isPackage = path.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".appx", StringComparison.OrdinalIgnoreCase);
        fixed (char* file = path)
        {
            uint index = 0;
            var fileInfo = new SIGNER_FILE_INFO { cbSize = (uint)sizeof(SIGNER_FILE_INFO), pwszFileName = file };
            var subject = new SIGNER_SUBJECT_INFO { cbSize = (uint)sizeof(SIGNER_SUBJECT_INFO), pdwIndex = &index, dwSubjectChoice = SIGNER_SUBJECT_FILE, pSignerFileInfo = &fileInfo };
            var store = new SIGNER_CERT_STORE_INFO { cbSize = (uint)sizeof(SIGNER_CERT_STORE_INFO), pSigningCert = cert.Handle, dwCertPolicy = SIGNER_CERT_POLICY_CHAIN };
            var signerCert = new SIGNER_CERT { cbSize = (uint)sizeof(SIGNER_CERT), dwCertChoice = SIGNER_CERT_STORE, pCertStoreInfo = &store };
            var signature = new SIGNER_SIGNATURE_INFO { cbSize = (uint)sizeof(SIGNER_SIGNATURE_INFO), algidHash = CALG_SHA_256, dwAttrChoice = SIGNER_NO_ATTR };
            IntPtr context = IntPtr.Zero;

            // Packages are signed through the APPX SIP, which needs the same arguments handed over once more.
            var parameters = new SIGNER_SIGN_EX2_PARAMS
            {
                pSubjectInfo = &subject, pSigningCert = &signerCert, pSignatureInfo = &signature, pSignerContext = &context,
            };
            var sip = new APPX_SIP_CLIENT_DATA { pSignerParams = &parameters };
            if (isPackage) parameters.pSipData = &sip;

            var hr = SignerSignEx2(0, &subject, &signerCert, &signature, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                isPackage ? &sip : null, &context, IntPtr.Zero, IntPtr.Zero);
            if (context != IntPtr.Zero) SignerFreeSignerContext(context);
            if (sip.pAppxSipState != IntPtr.Zero) Marshal.Release(sip.pAppxSipState);
            if (hr != 0) throw new CryptographicException($"signing {Path.GetFileName(path)} failed (0x{hr:X8})");
        }
        GC.KeepAlive(cert);
    }

    private const uint SIGNER_SUBJECT_FILE = 1;
    private const uint SIGNER_CERT_STORE = 2;
    private const uint SIGNER_CERT_POLICY_CHAIN = 2;
    private const uint SIGNER_NO_ATTR = 0;
    private const uint CALG_SHA_256 = 0x0000800c;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIGNER_FILE_INFO { public uint cbSize; public char* pwszFileName; public IntPtr hFile; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIGNER_SUBJECT_INFO { public uint cbSize; public uint* pdwIndex; public uint dwSubjectChoice; public SIGNER_FILE_INFO* pSignerFileInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIGNER_CERT_STORE_INFO { public uint cbSize; public IntPtr pSigningCert; public uint dwCertPolicy; public IntPtr hCertStore; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIGNER_CERT { public uint cbSize; public uint dwCertChoice; public SIGNER_CERT_STORE_INFO* pCertStoreInfo; public IntPtr hwnd; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIGNER_SIGNATURE_INFO
    {
        public uint cbSize; public uint algidHash; public uint dwAttrChoice; public IntPtr pAttrAuthcode;
        public IntPtr psAuthenticated; public IntPtr psUnauthenticated;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIGNER_SIGN_EX2_PARAMS
    {
        public uint dwFlags; public SIGNER_SUBJECT_INFO* pSubjectInfo; public SIGNER_CERT* pSigningCert;
        public SIGNER_SIGNATURE_INFO* pSignatureInfo; public IntPtr pProviderInfo; public uint dwTimestampFlags;
        public IntPtr pszAlgorithmOid; public IntPtr pwszTimestampURL; public IntPtr pCryptAttrs; public void* pSipData;
        public IntPtr* pSignerContext; public IntPtr pCryptoPolicy; public IntPtr pReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPX_SIP_CLIENT_DATA { public SIGNER_SIGN_EX2_PARAMS* pSignerParams; public IntPtr pAppxSipState; }

    [DllImport("mssign32.dll", ExactSpelling = true)]
    private static extern int SignerSignEx2(uint dwFlags, SIGNER_SUBJECT_INFO* pSubjectInfo, SIGNER_CERT* pSignerCert,
        SIGNER_SIGNATURE_INFO* pSignatureInfo, IntPtr pProviderInfo, uint dwTimestampFlags, IntPtr pszTimestampAlgorithmOid,
        IntPtr pwszHttpTimeStamp, IntPtr psRequest, void* pSipData, IntPtr* ppSignerContext, IntPtr pCryptoPolicy, IntPtr pReserved);

    [DllImport("mssign32.dll", ExactSpelling = true)]
    private static extern int SignerFreeSignerContext(IntPtr pSignerContext);
}
