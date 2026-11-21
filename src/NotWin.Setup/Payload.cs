using System.Buffers.Binary;
using System.Text;

namespace NotWin.Setup;

/// <summary>
/// The app rides at the end of NotWin-Setup.exe: [setup exe][payload.zip][zip length, int64 LE]["NWSETUP1"].
/// Windows ignores bytes after a PE image, so the exe still runs; cutting the tail off gives the uninstaller.
/// </summary>
public static class Payload
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("NWSETUP1");
    private const int FooterLength = 16;

    public static void Append(string stubExe, string zip, string output)
    {
        using var o = File.Create(output);
        using (var s = File.OpenRead(stubExe)) s.CopyTo(o);
        long length;
        using (var z = File.OpenRead(zip)) { length = z.Length; z.CopyTo(o); }
        Span<byte> footer = stackalloc byte[FooterLength];
        BinaryPrimitives.WriteInt64LittleEndian(footer, length);
        Magic.CopyTo(footer[8..]);
        o.Write(footer);
    }

    /// <summary>Where the zip starts and how long it is, or null for an exe with no payload (the uninstaller).</summary>
    public static (long Offset, long Length)? Find(string exe)
    {
        using var s = File.OpenRead(exe);
        if (s.Length < FooterLength) return null;
        Span<byte> footer = stackalloc byte[FooterLength];
        s.Seek(-FooterLength, SeekOrigin.End);
        s.ReadExactly(footer);
        if (!footer[8..].SequenceEqual(Magic)) return null;
        var length = BinaryPrimitives.ReadInt64LittleEndian(footer);
        var offset = s.Length - FooterLength - length;
        return length > 0 && offset > 0 ? (offset, length) : null;
    }

    public static void Extract(string exe, (long Offset, long Length) at, string zipOut)
    {
        using var s = File.OpenRead(exe);
        using var o = File.Create(zipOut);
        Copy(s, o, at.Offset, at.Length);
    }

    /// <summary>Writes the exe without its payload (the uninstaller kept in Program Files).</summary>
    public static void WriteStub(string exe, string output)
    {
        var at = Find(exe);
        using var s = File.OpenRead(exe);
        using var o = File.Create(output);
        Copy(s, o, 0, at?.Offset ?? s.Length);
    }

    private static void Copy(Stream from, Stream to, long offset, long count)
    {
        from.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[1 << 16];
        while (count > 0)
        {
            var n = from.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (n <= 0) throw new EndOfStreamException("setup file is cut short");
            to.Write(buffer, 0, n);
            count -= n;
        }
    }
}
