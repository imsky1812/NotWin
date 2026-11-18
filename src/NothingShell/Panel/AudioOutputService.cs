using NothingShell.Core.Panel;
using NothingShell.Native;

namespace NothingShell.Panel;

/// <summary>The output chip's data: active playback devices and switching the default (UI thread, COM STA).</summary>
public sealed class AudioOutputService
{
    public IReadOnlyList<AudioOutput> Outputs { get; private set; } = [];
    public string? CurrentId { get; private set; }

    /// <summary>Re-reads the devices (each time the panel opens: headphones may have come or gone).</summary>
    public void Refresh()
    {
        try
        {
            Outputs = AudioPolicyNative.ActiveRenderEndpoints().Select(e => new AudioOutput(e.Id, e.Name)).ToList();
            CurrentId = AudioPolicyNative.DefaultRenderId();
        }
        catch (Exception e)
        {
            Log.Error("reading playback devices failed", e);
            Outputs = [];
            CurrentId = null;
        }
    }

    public AudioOutput? Current => Outputs.FirstOrDefault(o => string.Equals(o.Id, CurrentId, StringComparison.OrdinalIgnoreCase)) is { Id: not null } c ? c : null;

    /// <summary>Switches to the next device. Returns the new one, or null if there is nothing to switch to.</summary>
    public AudioOutput? SwitchToNext()
    {
        if (AudioOutputs.Next(Outputs, CurrentId) is not { } next) return null;
        var ok = AudioPolicyNative.SetDefault(next.Id);
        Log.Info($"output switch ok={ok} devices={Outputs.Count}");
        if (!ok) return null;
        CurrentId = next.Id;
        return next;
    }
}
