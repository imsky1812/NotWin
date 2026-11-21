namespace NothingShell.Core.Launcher;

/// <summary>One app from Windows' UserAssist run history (used until Nothing Shell has its own launch history).</summary>
public readonly record struct UserAssistEntry(string Name, int RunCount, DateTimeOffset? LastRun);

/// <summary>
/// Decodes UserAssist registry values (<c>...\Explorer\UserAssist\{CEBFF5CD-...}\Count</c>): value names are ROT13,
/// data is a 72-byte record with the run count at offset 4 and the last-run FILETIME at offset 60.
/// </summary>
public static class UserAssistParser
{
    private const int MinLength = 68;

    public static string Rot13(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is >= 'a' and <= 'z') chars[i] = (char)('a' + (c - 'a' + 13) % 26);
            else if (c is >= 'A' and <= 'Z') chars[i] = (char)('A' + (c - 'A' + 13) % 26);
        }
        return new string(chars);
    }

    public static UserAssistEntry? Parse(string valueName, byte[] data)
    {
        if (data is null || data.Length < MinLength) return null;
        var count = BitConverter.ToInt32(data, 4);
        var ft = BitConverter.ToInt64(data, 60);
        DateTimeOffset? last = null;
        if (ft > 0)
        {
            try { last = DateTimeOffset.FromFileTime(ft); }
            catch (ArgumentOutOfRangeException) { /* garbage timestamp: treat as unknown */ }
        }
        return new UserAssistEntry(Rot13(valueName), count, last);
    }
}
