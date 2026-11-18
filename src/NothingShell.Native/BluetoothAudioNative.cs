using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NothingShell.Native;

/// <summary>
/// Connects / disconnects a paired Bluetooth audio device (buds, headphones, speakers) the way Windows' own audio
/// stack does: the Bluetooth audio driver's KS filter accepts the "one-shot reconnect / disconnect" property
/// (KSPROPSETID_BtAudio). Windows has no public "connect this paired device" API for audio. Returns false when no
/// matching filter answers; the caller then falls back to Windows' Bluetooth settings.
/// </summary>
public static class BluetoothAudioNative
{
    private static readonly Guid KsCategoryAudio = new("6994AD04-93EF-11D0-A3CC-00A0C9223196");
    private static readonly Guid KsPropSetIdBtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private const uint KsPropertyOneShotReconnect = 0, KsPropertyOneShotDisconnect = 1, KsPropertyTypeGet = 1;
    private const uint IoctlKsProperty = 0x002F0003;
    private const uint DigcfPresent = 0x2, DigcfDeviceInterface = 0x10;
    private const uint SpdrpFriendlyName = 0xC;

    /// <summary>Sends the one-shot to every Bluetooth audio filter whose friendly name contains <paramref name="deviceName"/>.</summary>
    public static bool SetConnected(string deviceName, bool connect)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return false;
        var any = false;
        var guid = KsCategoryAudio;
        var set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return false;
        try
        {
            for (uint i = 0; ; i++)
            {
                var iface = new SpDeviceInterfaceData { cbSize = (uint)Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref iface)) break;
                var info = new SpDevinfoData { cbSize = (uint)Marshal.SizeOf<SpDevinfoData>() };
                var path = InterfacePath(set, ref iface, ref info);
                if (path is null) continue;
                var instance = InstanceId(set, ref info) ?? "";
                if (!instance.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) continue; // Bluetooth audio only
                var name = FriendlyName(set, ref info) ?? "";
                if (!name.Contains(deviceName, StringComparison.OrdinalIgnoreCase)) continue;
                any |= SendOneShot(path, connect ? KsPropertyOneShotReconnect : KsPropertyOneShotDisconnect);
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return any;
    }

    private static bool SendOneShot(string path, uint id)
    {
        using var h = CreateFile(path, 0xC0000000 /* GENERIC_READ|WRITE */, 3 /* share read|write */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
        if (h.IsInvalid) return false;
        var prop = new KsProperty { Set = KsPropSetIdBtAudio, Id = id, Flags = KsPropertyTypeGet };
        return DeviceIoControl(h, IoctlKsProperty, ref prop, (uint)Marshal.SizeOf<KsProperty>(), IntPtr.Zero, 0, out _, IntPtr.Zero);
    }

    private static string? InterfacePath(IntPtr set, ref SpDeviceInterfaceData iface, ref SpDevinfoData info)
    {
        SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out var size, IntPtr.Zero);
        if (size == 0) return null;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6); // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize
            if (!SetupDiGetDeviceInterfaceDetail(set, ref iface, buf, size, out _, ref info)) return null;
            return Marshal.PtrToStringUni(buf + 4);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static string? InstanceId(IntPtr set, ref SpDevinfoData info)
    {
        var sb = new char[512];
        return SetupDiGetDeviceInstanceId(set, ref info, sb, (uint)sb.Length, out var len) ? new string(sb, 0, (int)Math.Max(0, len - 1)) : null;
    }

    private static string? FriendlyName(IntPtr set, ref SpDevinfoData info)
    {
        var buf = new byte[1024];
        if (!SetupDiGetDeviceRegistryProperty(set, ref info, SpdrpFriendlyName, out _, buf, (uint)buf.Length, out var len)) return null;
        return System.Text.Encoding.Unicode.GetString(buf, 0, (int)len).TrimEnd('\0');
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData { public uint cbSize; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KsProperty { public Guid Set; public uint Id; public uint Flags; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SpDeviceInterfaceData data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, uint size, out uint required, IntPtr devInfo);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, uint size, out uint required, ref SpDevinfoData devInfo);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SpDevinfoData info, char[] id, uint size, out uint required);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref SpDevinfoData info, uint property, out uint type, byte[] buffer, uint size, out uint required);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref KsProperty inBuf, uint inSize, IntPtr outBuf, uint outSize, out uint returned, IntPtr overlapped);
}
