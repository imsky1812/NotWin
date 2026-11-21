namespace NothingShell.Core.Launcher;

/// <summary>Fixed catalogue of common Windows Settings pages (spec §6.2 sources) with search keywords.</summary>
public static class SettingsPages
{
    public static IReadOnlyList<LaunchItem> All { get; } =
    [
        P("Display", "display", "brightness", "resolution", "scale", "monitor"),
        P("Night light", "nightlight"),
        P("Sound", "sound", "volume", "speakers", "microphone", "audio"),
        P("Notifications", "notifications"),
        P("Focus", "focus"),
        P("Power & battery", "powersleep", "sleep", "battery"),
        P("Storage", "storagesense", "disk", "space"),
        P("Clipboard", "clipboard"),
        P("About", "about", "pc name", "specs"),
        P("Bluetooth & devices", "bluetooth", "headphones", "buds"),
        P("Printers & scanners", "printers"),
        P("Mouse", "mousetouchpad", "touchpad", "cursor"),
        P("Wi-Fi", "network-wifi", "wireless", "internet"),
        P("Network & internet", "network", "ethernet"),
        P("VPN", "network-vpn"),
        P("Mobile hotspot", "network-mobilehotspot"),
        P("Airplane mode", "network-airplanemode"),
        P("Personalization", "personalization"),
        P("Background", "personalization-background", "wallpaper"),
        P("Colors", "colors", "dark mode", "theme"),
        P("Lock screen", "lockscreen"),
        P("Installed apps", "appsfeatures", "uninstall", "programs"),
        P("Default apps", "defaultapps", "browser"),
        P("Startup apps", "startupapps"),
        P("Your info", "yourinfo", "account"),
        P("Sign-in options", "signinoptions", "password", "pin", "windows hello"),
        P("Date & time", "dateandtime", "clock", "time zone"),
        P("Language & region", "regionlanguage", "keyboard"),
        P("Gaming", "gaming-gamemode", "game mode"),
        P("Accessibility", "easeofaccess"),
        P("Privacy & security", "privacy", "permissions"),
        P("Windows Update", "windowsupdate", "update"),
    ];

    private static LaunchItem P(string name, string page, params string[] keywords)
    {
        var uri = "ms-settings:" + page;
        return new LaunchItem(uri, name, LaunchKind.Setting, uri, keywords);
    }
}
