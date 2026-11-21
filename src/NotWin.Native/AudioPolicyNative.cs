using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace NotWin.Native;

/// <summary>
/// Playback devices for the panel's output chip: list active render endpoints, read the default, and switch the
/// default through the (undocumented but long-stable) IPolicyConfig interface used by Windows' own sound settings.
/// Call on a COM-initialised (STA) thread. All vtable slots are documented next to each call.
/// </summary>
public static unsafe class AudioPolicyNative
{
    private static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid PolicyConfigClsid = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");
    private static readonly Guid PolicyConfigIid = new("f8679f50-850a-41cf-9c72-430f290290c8");
    private static readonly PROPERTYKEY FriendlyName = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
    private const uint DeviceStateActive = 0x1;

    public static IReadOnlyList<(string Id, string Name)> ActiveRenderEndpoints()
    {
        var result = new List<(string, string)>();
        var e = Enumerator();
        if (e is null) return result;
        void* coll = null;
        try
        {
            // IMMDeviceEnumerator 3: EnumAudioEndpoints(EDataFlow, DWORD, IMMDeviceCollection**)
            var hr = ((delegate* unmanaged[Stdcall]<void*, EDataFlow, uint, void**, HRESULT>)(*(void***)e)[3])(e, EDataFlow.eRender, DeviceStateActive, &coll);
            if (hr.Failed || coll is null) return result;
            uint count = 0;
            ((delegate* unmanaged[Stdcall]<void*, uint*, HRESULT>)(*(void***)coll)[3])(coll, &count); // GetCount
            for (uint i = 0; i < count; i++)
            {
                void* dev = null;
                if (((delegate* unmanaged[Stdcall]<void*, uint, void**, HRESULT>)(*(void***)coll)[4])(coll, i, &dev).Failed || dev is null) continue; // Item
                try
                {
                    var id = DeviceId(dev);
                    if (id is not null) result.Add((id, DeviceName(dev) ?? "Output"));
                }
                finally { ((IUnknown*)dev)->Release(); }
            }
        }
        catch { /* list stays partial */ }
        finally
        {
            if (coll is not null) ((IUnknown*)coll)->Release();
            ((IUnknown*)e)->Release();
        }
        return result;
    }

    public static string? DefaultRenderId()
    {
        var e = Enumerator();
        if (e is null) return null;
        void* dev = null;
        try
        {
            // IMMDeviceEnumerator 4: GetDefaultAudioEndpoint(EDataFlow, ERole, IMMDevice**)
            var hr = ((delegate* unmanaged[Stdcall]<void*, EDataFlow, ERole, void**, HRESULT>)(*(void***)e)[4])(e, EDataFlow.eRender, ERole.eConsole, &dev);
            return hr.Failed || dev is null ? null : DeviceId(dev);
        }
        catch { return null; }
        finally
        {
            if (dev is not null) ((IUnknown*)dev)->Release();
            ((IUnknown*)e)->Release();
        }
    }

    /// <summary>Makes <paramref name="id"/> the default playback device for all three roles.</summary>
    public static bool SetDefault(string id)
    {
        void* policy = null;
        try
        {
            var clsid = PolicyConfigClsid;
            var iid = PolicyConfigIid;
            if (PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_ALL, &iid, &policy).Failed || policy is null) return false;
            var ok = true;
            fixed (char* p = id)
                foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
                {
                    // IPolicyConfig 13: SetDefaultEndpoint(LPCWSTR deviceId, ERole role)
                    var hr = ((delegate* unmanaged[Stdcall]<void*, char*, ERole, HRESULT>)(*(void***)policy)[13])(policy, p, role);
                    ok &= hr.Succeeded;
                }
            return ok;
        }
        catch { return false; }
        finally { if (policy is not null) ((IUnknown*)policy)->Release(); }
    }

    private static void* Enumerator()
    {
        var clsid = MMDeviceEnumeratorClsid;
        var iid = IMMDeviceEnumerator.IID_Guid;
        void* raw;
        return PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, &raw).Failed ? null : raw;
    }

    private static string? DeviceId(void* dev)
    {
        PWSTR s = default;
        // IMMDevice 5: GetId(LPWSTR*)
        if (((delegate* unmanaged[Stdcall]<void*, PWSTR*, HRESULT>)(*(void***)dev)[5])(dev, &s).Failed || s.Value is null) return null;
        try { return new string(s.Value); }
        finally { PInvoke.CoTaskMemFree(s.Value); }
    }

    private static string? DeviceName(void* dev)
    {
        void* store = null;
        PROPVARIANT pv = default;
        try
        {
            // IMMDevice 4: OpenPropertyStore(STGM_READ = 0, IPropertyStore**)
            if (((delegate* unmanaged[Stdcall]<void*, uint, void**, HRESULT>)(*(void***)dev)[4])(dev, 0, &store).Failed || store is null) return null;
            var key = FriendlyName;
            // IPropertyStore 5: GetValue(REFPROPERTYKEY, PROPVARIANT*)
            if (((delegate* unmanaged[Stdcall]<void*, PROPERTYKEY*, PROPVARIANT*, HRESULT>)(*(void***)store)[5])(store, &key, &pv).Failed) return null;
            PWSTR str;
            if (PInvoke.PropVariantToStringAlloc(&pv, &str).Failed || str.Value is null) return null;
            try { return new string(str.Value); }
            finally { PInvoke.CoTaskMemFree(str.Value); }
        }
        catch { return null; }
        finally
        {
            PInvoke.PropVariantClear(&pv);
            if (store is not null) ((IUnknown*)store)->Release();
        }
    }
}
