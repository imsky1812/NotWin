using NothingShell.Core.Panel;
using NothingShell.Native;

namespace NothingShell.Panel;

/// <summary>The output chip's data: active playback devices and switching the default (UI thread, COM STA).</summary>
public sealed class AudioOutputService
{
    private readonly Func<IReadOnlyList<(string Id, string Name)>> _enumerate;
    private readonly Func<string?> _defaultId;
    private readonly Func<string, bool> _setDefault;

    /// <summary>Defaults: Core Audio (<see cref="AudioPolicyNative"/>). Tests inject the three functions.</summary>
    public AudioOutputService(Func<IReadOnlyList<(string Id, string Name)>>? enumerate = null, Func<string?>? defaultId = null,
        Func<string, bool>? setDefault = null)
    {
        _enumerate = enumerate ?? AudioPolicyNative.ActiveRenderEndpoints;
        _defaultId = defaultId ?? AudioPolicyNative.DefaultRenderId;
        _setDefault = setDefault ?? AudioPolicyNative.SetDefault;
    }

    public IReadOnlyList<AudioOutput> Outputs { get; private set; } = [];
    public string? CurrentId { get; private set; }

    /// <summary>Re-reads the devices (panel open, default device changed, and before every switch).</summary>
    public void Refresh()
    {
        try
        {
            Outputs = _enumerate().Select(e => new AudioOutput(e.Id, e.Name)).ToList();
            CurrentId = _defaultId();
        }
        catch (Exception e)
        {
            Log.Error("reading playback devices failed", e);
            Outputs = [];
            CurrentId = null;
        }
    }

    public AudioOutput? Current =>
        Outputs.FirstOrDefault(o => string.Equals(o.Id, CurrentId, StringComparison.OrdinalIgnoreCase)) is { Id: not null } c ? c : null;

    /// <summary>
    /// Switches to the next device. The list is re-read first, so headphones unplugged while the panel was open are
    /// never targeted. Returns the new device, or null if there is nothing to switch to.
    /// </summary>
    public AudioOutput? SwitchToNext()
    {
        Refresh();
        if (AudioOutputs.Next(Outputs, CurrentId) is not { } next) return null;
        var ok = _setDefault(next.Id);
        Log.Info($"output switch ok={ok} devices={Outputs.Count}");
        if (!ok) return null;
        CurrentId = next.Id;
        return next;
    }
}
