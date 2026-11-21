using System.Runtime.InteropServices;
using System.Text;

namespace NotWin.Shell;

/// <summary>Whether this process runs with the sparse package identity (needed for UserNotificationListener).</summary>
public static class PackageIdentity
{
    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorInsufficientBuffer = 122;

    public static bool HasIdentity(out string? fullName)
    {
        fullName = null;
        try
        {
            var len = 0;
            var rc = GetCurrentPackageFullName(ref len, null);
            if (rc == AppModelErrorNoPackage) return false;
            if (rc != ErrorInsufficientBuffer || len <= 0) return false;
            var sb = new StringBuilder(len);
            rc = GetCurrentPackageFullName(ref len, sb);
            if (rc != 0) return false;
            fullName = sb.ToString();
            return true;
        }
        catch (Exception) { return false; } // API missing or blocked: treat as no identity
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);
}
