using System.Text;
using NotWin.Core.Panel;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.WiFi;

namespace NotWin.Native;

/// <summary>
/// Wi-Fi networks through the native WLAN API (works for desktop apps; since Windows 11 24H2 the scan results need
/// "Let desktop apps access your location", reported as <see cref="WifiScanStatus.LocationDenied"/>).
/// SSIDs are never logged by this class.
/// </summary>
public static unsafe class WlanNative
{
    private const uint ErrorAccessDenied = 5;
    private const uint FlagConnected = 0x1, FlagHasProfile = 0x2;

    public static (WifiScanStatus Status, IReadOnlyList<WifiNetwork> Networks) GetNetworks()
    {
        HANDLE h = default;
        WLAN_INTERFACE_INFO_LIST* ifaces = null;
        try
        {
            uint ver;
            if (PInvoke.WlanOpenHandle(2, null, &ver, &h) != 0) return (WifiScanStatus.NoAdapter, []);
            if (PInvoke.WlanEnumInterfaces(h, null, &ifaces) != 0 || ifaces is null || ifaces->dwNumberOfItems == 0)
                return (WifiScanStatus.NoAdapter, []);

            var iface = ifaces->InterfaceInfo[0];
            if (iface.isState == WLAN_INTERFACE_STATE.wlan_interface_state_not_ready) return (WifiScanStatus.RadioOff, []);
            var guid = iface.InterfaceGuid;
            PInvoke.WlanScan(h, &guid, null, null, null); // refresh in the background; results below are the cached list

            WLAN_AVAILABLE_NETWORK_LIST* list = null;
            var rc = PInvoke.WlanGetAvailableNetworkList(h, &guid, 0, null, &list);
            if (rc == ErrorAccessDenied) return (WifiScanStatus.LocationDenied, []);
            if (rc != 0 || list is null) return (WifiScanStatus.Error, []);
            try
            {
                var result = new List<WifiNetwork>();
                var items = list->Network.AsSpan((int)list->dwNumberOfItems);
                foreach (ref readonly var n in items)
                {
                    var len = (int)Math.Min(n.dot11Ssid.uSSIDLength, 32u);
                    var ssid = Encoding.UTF8.GetString(n.dot11Ssid.ucSSID.AsReadOnlySpan()[..len]);
                    result.Add(new WifiNetwork(ssid, (int)n.wlanSignalQuality, n.bSecurityEnabled,
                        (n.dwFlags & FlagHasProfile) != 0, (n.dwFlags & FlagConnected) != 0));
                }
                return (WifiScanStatus.Ok, result);
            }
            finally { PInvoke.WlanFreeMemory(list); }
        }
        catch { return (WifiScanStatus.Error, []); }
        finally
        {
            if (ifaces is not null) PInvoke.WlanFreeMemory(ifaces);
            if (!h.IsNull) PInvoke.WlanCloseHandle(h, null);
        }
    }

    /// <summary>
    /// Whether the first Wi-Fi interface is connected. Reads only the interface state, which is not location-gated:
    /// no scan, no Windows location prompt (the tile uses this on every panel open).
    /// </summary>
    public static bool IsConnected()
    {
        HANDLE h = default;
        WLAN_INTERFACE_INFO_LIST* ifaces = null;
        try
        {
            uint ver;
            if (PInvoke.WlanOpenHandle(2, null, &ver, &h) != 0) return false;
            if (PInvoke.WlanEnumInterfaces(h, null, &ifaces) != 0 || ifaces is null || ifaces->dwNumberOfItems == 0) return false;
            return ifaces->InterfaceInfo[0].isState == WLAN_INTERFACE_STATE.wlan_interface_state_connected;
        }
        catch { return false; }
        finally
        {
            if (ifaces is not null) PInvoke.WlanFreeMemory(ifaces);
            if (!h.IsNull) PInvoke.WlanCloseHandle(h, null);
        }
    }

    /// <summary>Connects with the saved profile named <paramref name="profileName"/> (Windows names profiles after the SSID).</summary>
    public static bool Connect(string profileName) => WithFirstInterface((h, guid) =>
    {
        fixed (char* p = profileName)
        {
            var prm = new WLAN_CONNECTION_PARAMETERS
            {
                wlanConnectionMode = WLAN_CONNECTION_MODE.wlan_connection_mode_profile,
                strProfile = p,
                dot11BssType = DOT11_BSS_TYPE.dot11_BSS_type_any,
            };
            return PInvoke.WlanConnect(h, &guid, &prm, null) == 0;
        }
    });

    /// <summary>Saves <paramref name="profileXml"/> (an open network's profile) for this user, then connects to it.</summary>
    public static bool AddProfileAndConnect(string profileXml, string ssid) => WithFirstInterface((h, guid) =>
    {
        uint reason;
        fixed (char* xml = profileXml)
            if (PInvoke.WlanSetProfile(h, &guid, 0x2 /* WLAN_PROFILE_USER */, xml, null, true, null, &reason) != 0) return false;
        return Connect(ssid);
    });

    private static bool WithFirstInterface(Func<HANDLE, Guid, bool> act)
    {
        HANDLE h = default;
        WLAN_INTERFACE_INFO_LIST* ifaces = null;
        try
        {
            uint ver;
            if (PInvoke.WlanOpenHandle(2, null, &ver, &h) != 0) return false;
            if (PInvoke.WlanEnumInterfaces(h, null, &ifaces) != 0 || ifaces is null || ifaces->dwNumberOfItems == 0) return false;
            return act(h, ifaces->InterfaceInfo[0].InterfaceGuid);
        }
        catch { return false; }
        finally
        {
            if (ifaces is not null) PInvoke.WlanFreeMemory(ifaces);
            if (!h.IsNull) PInvoke.WlanCloseHandle(h, null);
        }
    }
}
