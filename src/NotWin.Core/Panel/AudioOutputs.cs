namespace NotWin.Core.Panel;

/// <summary>An active playback device (Core Audio endpoint id + friendly name).</summary>
public readonly record struct AudioOutput(string Id, string Name);

/// <summary>The panel's output chip (spec §6.3): a click switches to the next active playback device.</summary>
public static class AudioOutputs
{
    /// <summary>The device after <paramref name="currentId"/> (wrapping); the first one if the current is gone; null when there is nothing to switch to.</summary>
    public static AudioOutput? Next(IReadOnlyList<AudioOutput> outputs, string? currentId)
    {
        if (outputs.Count == 0) return null;
        var i = -1;
        for (var k = 0; k < outputs.Count; k++)
            if (string.Equals(outputs[k].Id, currentId, StringComparison.OrdinalIgnoreCase)) { i = k; break; }
        if (i < 0) return outputs[0];
        if (outputs.Count == 1) return null;
        return outputs[(i + 1) % outputs.Count];
    }

    /// <summary>Chip label: the friendly name up to its first " (" ("Speakers (Realtek Audio)" → "Speakers").</summary>
    public static string ShortName(string fullName)
    {
        var s = (fullName ?? "").Trim();
        if (s.Length == 0) return "Output";
        var p = s.IndexOf(" (", StringComparison.Ordinal);
        return p > 0 ? s[..p] : s;
    }
}
