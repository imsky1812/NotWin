namespace NothingShell.Core.Notch;

/// <summary>
/// The notch's 10×4 dot equalizer, driven by the real output level (scaled to the track's own loudness) (user feedback 2026-11-19: random heights read as
/// noise). Each tick (50 ms) the newest level lights the two centre columns and older levels ripple outwards, one
/// column per tick, so the shape is mirrored and follows the music. Rises are instant; falls are gradual (about half a
/// second from full to the floor). Silence leaves a baseline of one dot per column, without red.
/// </summary>
public sealed class EqualizerModel
{
    public const int Columns = 10, Rows = 4;
    private const int Half = Columns / 2;
    // Levels are measured against a slowly fading recent peak (halves in ~7 s at 20 steps/s), over a 24 dB range, so
    // quiet and loud tracks both fill the bars (measured live: Spotify peaks of 0.01-0.05 sat on the baseline with a
    // fixed -36 dB scale). Below the noise floor nothing is amplified.
    private const double RangeDb = 24, RefDecay = 0.995, NoiseFloor = 0.002, Falloff = 0.06, Decay = 0.09;
    private double _ref;

    private readonly double[] _history = new double[Half]; // newest first: distance 0 = centre columns
    private readonly double[] _value = new double[Columns];
    private readonly int[] _heights = Enumerable.Repeat(1, Columns).ToArray();
    private int _max = 1;

    public IReadOnlyList<int> Heights => _heights;

    /// <summary>
    /// The top dot of the tallest columns is red: the peak, like the mockup's red tops but used sparingly (one accent).
    /// The silent baseline has none.
    /// </summary>
    public bool RedTop(int column) => _heights[column] >= 2 && _heights[column] == _max;

    /// <summary>A peak (0..1, Core Audio meter) as 0..1: decibels below <paramref name="reference"/>, 24 dB = 0.</summary>
    public static double Level(float peak, double reference)
    {
        if (peak <= 0 || reference <= 0) return 0;
        var db = 20 * Math.Log10(Math.Min(peak / reference, 1));
        return Math.Clamp((db + RangeDb) / RangeDb, 0, 1);
    }

    public void Step(float peak)
    {
        Array.Copy(_history, 0, _history, 1, Half - 1);
        _ref = Math.Max(peak, _ref * RefDecay);
        _history[0] = _ref < NoiseFloor ? 0 : Level(peak, _ref);
        for (var c = 0; c < Columns; c++)
        {
            var d = c < Half ? Half - 1 - c : c - Half;
            var target = _history[d] * (1 - Falloff * d);
            _value[c] = target >= _value[c] ? target : Math.Max(target, _value[c] - Decay);
            _heights[c] = Math.Clamp((int)Math.Round(_value[c] * Rows), 1, Rows);
        }
        _max = _heights.Max();
    }
}
