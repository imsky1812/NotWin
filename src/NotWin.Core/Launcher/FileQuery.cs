using System.Text;

namespace NotWin.Core.Launcher;

/// <summary>Builds the Windows Search (SystemIndex) SQL for a file-name search. Escapes everything the user types.</summary>
public static class FileQuery
{
    public const int MaxQueryLength = 64;

    /// <summary>Null for an empty/whitespace query. Shortcuts (.lnk) are excluded: Start Menu items are already apps.</summary>
    public static string? Build(string query, int top = 20)
    {
        var q = query?.Trim() ?? "";
        if (q.Length == 0) return null;
        if (q.Length > MaxQueryLength) q = q[..MaxQueryLength];
        return $"SELECT TOP {top} System.ItemPathDisplay, System.ItemNameDisplay, System.ItemType FROM SystemIndex " +
               $"WHERE SCOPE='file:' AND System.ItemNameDisplay LIKE '%{EscapeLiteral(q)}%' AND System.ItemType <> '.lnk' " +
               "ORDER BY System.DateModified DESC";
    }

    /// <summary>Quotes doubled; LIKE wildcards (<c>%</c>, <c>_</c>) and <c>[</c> wrapped in brackets.</summary>
    public static string EscapeLiteral(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\'': sb.Append("''"); break;
                case '[': sb.Append("[[]"); break;
                case '%': sb.Append("[%]"); break;
                case '_': sb.Append("[_]"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
