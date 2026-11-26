using System.Globalization;

namespace NotWin.Core.Widgets;

/// <summary>
/// Screen time (Plan 13): minutes at the PC per local day, while NotWin runs. A sample credits the time since the
/// previous one when the session is unlocked, the last input was under 5 minutes ago and the gap is at most 2 minutes
/// (a longer gap means sleep or NotWin was closed: nothing is credited). Whole minutes go to the sample's day; the
/// remaining seconds carry over. Only the last 8 days are kept.
/// </summary>
public sealed class ScreenTimeLog
{
    public const int KeepDays = 8;
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(2), MaxIdle = TimeSpan.FromMinutes(5);
    private readonly Dictionary<DateOnly, int> _days = [];
    private DateTime? _last;
    private double _carrySeconds;

    public ScreenTimeLog(IReadOnlyDictionary<string, int>? saved)
    {
        if (saved is null) return;
        foreach (var (key, minutes) in saved)
            if (DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && minutes > 0)
                _days[day] = minutes;
    }

    /// <summary>Records a sample; true when today's total changed.</summary>
    public bool Sample(DateTime localNow, bool unlocked, TimeSpan idle)
    {
        var last = _last;
        _last = localNow;
        if (last is not { } prev) return false;
        var gap = localNow - prev;
        if (gap <= TimeSpan.Zero || gap > MaxGap || !unlocked || idle >= MaxIdle)
        {
            _carrySeconds = 0;
            return false;
        }
        _carrySeconds += gap.TotalSeconds;
        var whole = (int)(_carrySeconds / 60);
        if (whole == 0) return false;
        _carrySeconds -= whole * 60;
        var day = DateOnly.FromDateTime(localNow);
        _days[day] = _days.GetValueOrDefault(day) + whole;
        return true;
    }

    public int Today(DateTime localNow) => _days.GetValueOrDefault(DateOnly.FromDateTime(localNow));

    /// <summary>The mean of the 7 days before today that have any time; 0 when none do.</summary>
    public int Average7(DateTime localNow)
    {
        var today = DateOnly.FromDateTime(localNow);
        var values = Enumerable.Range(1, 7).Select(i => _days.GetValueOrDefault(today.AddDays(-i))).Where(m => m > 0).ToList();
        return values.Count == 0 ? 0 : (int)Math.Round(values.Average());
    }

    /// <summary>Seven days, oldest first, ending today; days without data are 0.</summary>
    public IReadOnlyList<(DateOnly Day, int Minutes)> Week(DateTime localNow)
    {
        var today = DateOnly.FromDateTime(localNow);
        return Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6)).Select(d => (d, _days.GetValueOrDefault(d))).ToList();
    }

    /// <summary>What to save: the last <see cref="KeepDays"/> days (today included), keyed "yyyy-MM-dd".</summary>
    public IReadOnlyDictionary<string, int> ToSave(DateTime localNow)
    {
        var oldest = DateOnly.FromDateTime(localNow).AddDays(-(KeepDays - 1));
        return _days.Where(kv => kv.Key >= oldest)
            .ToDictionary(kv => kv.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), kv => kv.Value);
    }

    /// <summary>"6H20", "0H05".</summary>
    public static string Format(int minutes) =>
        string.Create(CultureInfo.InvariantCulture, $"{minutes / 60}H{minutes % 60:00}");
}

/// <summary><c>screentime.json</c> in the data folder: { "yyyy-MM-dd": minutes }. Not synced (Plan 13).</summary>
public static class ScreenTimeStore
{
    public const string FileName = "screentime.json";

    /// <summary>The saved days, or null when there is no file or it can't be read.</summary>
    public static Dictionary<string, int>? Load(string dataDir)
    {
        try
        {
            var path = Path.Combine(dataDir, FileName);
            return File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public static void Save(string dataDir, IReadOnlyDictionary<string, int> days) => JsonFile.WriteAtomic(dataDir, FileName, days);
}
