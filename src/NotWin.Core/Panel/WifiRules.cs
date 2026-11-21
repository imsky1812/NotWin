using System.Security;

namespace NotWin.Core.Panel;

public sealed record WifiNetwork(string Ssid, int SignalPercent, bool Secured, bool HasProfile, bool Connected);

public enum WifiAction { None, Connect, ConnectWithNewOpenProfile, OpenSettings }

public enum WifiScanStatus { Ok, NoAdapter, RadioOff, LocationDenied, Error }

/// <summary>The Wi-Fi list in the panel (spec §6.3): ordering, what a click does, and the hint rows.</summary>
public static class WifiRules
{
    /// <summary>Connected first, then saved networks, then by signal; one row per SSID (strongest kept); hidden SSIDs dropped.</summary>
    public static IReadOnlyList<WifiNetwork> Order(IEnumerable<WifiNetwork> networks) =>
        networks
            .Where(n => !string.IsNullOrWhiteSpace(n.Ssid))
            .GroupBy(n => n.Ssid, StringComparer.Ordinal)
            .Select(g =>
            {
                var best = g.MaxBy(n => n.SignalPercent)!;
                return best with { HasProfile = g.Any(n => n.HasProfile), Connected = g.Any(n => n.Connected) };
            })
            .OrderByDescending(n => n.Connected)
            .ThenByDescending(n => n.HasProfile)
            .ThenByDescending(n => n.SignalPercent)
            .ThenBy(n => n.Ssid, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Saved networks connect; an open network without a profile gets one and connects; a secured network without a
    /// profile opens Windows' Wi-Fi settings (we never ask for a password ourselves).
    /// </summary>
    public static WifiAction ActionFor(WifiNetwork n) =>
        n.Connected ? WifiAction.None
        : n.HasProfile ? WifiAction.Connect
        : n.Secured ? WifiAction.OpenSettings
        : WifiAction.ConnectWithNewOpenProfile;

    /// <summary>The hint row for a scan that returned nothing usable, or null when the list is fine.</summary>
    public static string? Hint(WifiScanStatus s) => s switch
    {
        WifiScanStatus.LocationDenied => "Let desktop apps use location to list networks",
        WifiScanStatus.RadioOff => "Wi-Fi is off",
        WifiScanStatus.NoAdapter => "No Wi-Fi adapter",
        WifiScanStatus.Error => "Couldn't list networks",
        _ => null,
    };

    /// <summary>
    /// The Wi-Fi tile's small line, from data that needs no scan (a scan is location-gated and shows Windows' location
    /// prompt / indicator, so it only happens when the user expands the list). Connected but SSID withheld → "Connected".
    /// </summary>
    public static string TileSubline(bool? radioOn, bool connected, string? ssid) => radioOn switch
    {
        null => "Unavailable",
        false => "Off",
        _ when !connected => "Not connected",
        _ => string.IsNullOrWhiteSpace(ssid) ? "Connected" : ssid,
    };

    /// <summary>A minimal WLAN profile for an open (no password) network.</summary>
    public static string OpenProfileXml(string ssid)
    {
        var name = SecurityElement.Escape(ssid);
        return "<?xml version=\"1.0\"?>" +
               "<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">" +
               $"<name>{name}</name><SSIDConfig><SSID><name>{name}</name></SSID></SSIDConfig>" +
               "<connectionType>ESS</connectionType><connectionMode>manual</connectionMode>" +
               "<MSM><security><authEncryption><authentication>open</authentication><encryption>none</encryption>" +
               "<useOneX>false</useOneX></authEncryption></security></MSM></WLANProfile>";
    }
}
