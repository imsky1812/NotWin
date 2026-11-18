using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
using Windows.Win32.System.Com;

namespace NothingShell.Native;

/// <summary>
/// Master volume / mute of the default render endpoint, pushed by Core Audio (no polling).
///
/// CsWin32 runs here with allowMarshaling=false, so the COM interfaces are plain vtable structs and there is no RCW/CCW
/// machinery. Calling them is easy; the two callback interfaces (IAudioEndpointVolumeCallback, IMMNotificationClient)
/// are hand-built native COM objects instead: a small unmanaged block { vtable*, refcount, GCHandle, IID } and a static
/// vtable whose slots are [UnmanagedCallersOnly] methods. That is about 100 lines, needs no ComWrappers and no scoped
/// marshaling exception, and is trim/AOT friendly. The callbacks arrive on audio-engine threads: they only raise
/// events; every COM call (including <see cref="Rebind"/>) must be made on the owning (UI) thread.
/// </summary>
public sealed unsafe class AudioVolumeWatcher : IDisposable
{
    private static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IUnknownIid = new("00000000-0000-0000-C000-000000000046");
    private const int SOk = 0;
    private const int ENoInterface = unchecked((int)0x80004002);

    private IMMDeviceEnumerator* _enumerator;
    private IAudioEndpointVolume* _volume;
    private Callback* _volumeCb;
    private Callback* _deviceCb;
    private bool _disposed;

    /// <summary>Volume (0-100) and mute changed. Raised on an audio thread.</summary>
    public event Action<int, bool>? VolumeChanged;

    /// <summary>The default render device changed. Raised on an audio thread; call <see cref="Rebind"/> on the UI thread.</summary>
    public event Action? DefaultDeviceChanged;

    /// <summary>Diagnostics sink (the native layer has no logger of its own).</summary>
    public Action<string, Exception?>? OnError { get; set; }

    /// <summary>Creates the enumerator and registers the default-device listener. Returns false if Core Audio is unavailable.</summary>
    public bool Start()
    {
        try
        {
            var clsid = MMDeviceEnumeratorClsid;
            var iid = IMMDeviceEnumerator.IID_Guid;
            void* raw;
            PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, &raw).ThrowOnFailure();
            _enumerator = (IMMDeviceEnumerator*)raw;

            _deviceCb = Callback.Create(this, DeviceVtable, IMMNotificationClient.IID_Guid);
            _volumeCb = Callback.Create(this, VolumeVtable, IAudioEndpointVolumeCallback.IID_Guid);
            var hr = ((delegate* unmanaged[Stdcall]<IMMDeviceEnumerator*, IMMNotificationClient*, HRESULT>)(*(void***)_enumerator)[6])(_enumerator, (IMMNotificationClient*)_deviceCb);
            if (hr.Failed) { OnError?.Invoke($"RegisterEndpointNotificationCallback failed 0x{hr.Value:X8}", null); return false; }
            return true;
        }
        catch (Exception e)
        {
            OnError?.Invoke("audio watcher start failed", e);
            return false;
        }
    }

    /// <summary>(Re)attaches to the current default render device. Returns its volume, or null when there is none. UI thread.</summary>
    public (int Percent, bool Muted)? Rebind()
    {
        if (_disposed || _enumerator is null) return null;
        Detach();
        IMMDevice* device = null;
        try
        {
            var hr = ((delegate* unmanaged[Stdcall]<IMMDeviceEnumerator*, EDataFlow, ERole, IMMDevice**, HRESULT>)(*(void***)_enumerator)[4])(_enumerator, EDataFlow.eRender, ERole.eConsole, &device);
            if (hr.Failed || device is null) return Fail("GetDefaultAudioEndpoint", hr); // e.g. no render device

            var iid = IAudioEndpointVolume.IID_Guid;
            void* raw = null;
            hr = ((delegate* unmanaged[Stdcall]<IMMDevice*, Guid*, CLSCTX, void*, void**, HRESULT>)(*(void***)device)[3])(device, &iid, CLSCTX.CLSCTX_ALL, null, &raw);
            if (hr.Failed || raw is null) return Fail("Activate(IAudioEndpointVolume)", hr);
            _volume = (IAudioEndpointVolume*)raw;

            hr = ((delegate* unmanaged[Stdcall]<IAudioEndpointVolume*, IAudioEndpointVolumeCallback*, HRESULT>)(*(void***)_volume)[3])(_volume, (IAudioEndpointVolumeCallback*)_volumeCb);
            if (hr.Failed) { _volume->Release(); _volume = null; return Fail("RegisterControlChangeNotify", hr); }

            float level = 0;
            BOOL mute = default;
            hr = ((delegate* unmanaged[Stdcall]<IAudioEndpointVolume*, float*, HRESULT>)(*(void***)_volume)[9])(_volume, &level);
            if (hr.Failed) return Fail("GetMasterVolumeLevelScalar", hr);
            hr = ((delegate* unmanaged[Stdcall]<IAudioEndpointVolume*, BOOL*, HRESULT>)(*(void***)_volume)[15])(_volume, &mute);
            if (hr.Failed) return Fail("GetMute", hr);
            _failLogged = false;
            return (ToPercent(level), mute);
        }
        catch (Exception e)
        {
            OnError?.Invoke("audio rebind failed (no default render device?)", e);
            Detach();
            return null;
        }
        finally
        {
            if (device is not null) device->Release();
        }
    }

    // The generated HRESULT-returning overloads are private and the public ones throw, so the checked calls go through
    // the vtable slots directly (IMMDeviceEnumerator: 4 GetDefaultAudioEndpoint, 6 RegisterEndpointNotificationCallback;
    // IMMDevice: 3 Activate; IAudioEndpointVolume: 3 RegisterControlChangeNotify, 9 GetMasterVolumeLevelScalar, 15 GetMute).
    private bool _failLogged;

    // Logs once per failure streak (default-device flapping must not spam); returns null = "no usable endpoint".
    private (int Percent, bool Muted)? Fail(string call, HRESULT hr)
    {
        if (!_failLogged) { _failLogged = true; OnError?.Invoke($"audio: {call} failed 0x{hr.Value:X8}", null); }
        Detach();
        return null;
    }

    private void Detach()
    {
        if (_volume is null) return;
        try { _volume->UnregisterControlChangeNotify((IAudioEndpointVolumeCallback*)_volumeCb); }
        catch (Exception e) { OnError?.Invoke("audio unregister failed", e); }
        _volume->Release();
        _volume = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
        if (_enumerator is not null)
        {
            try { _enumerator->UnregisterEndpointNotificationCallback((IMMNotificationClient*)_deviceCb); }
            catch (Exception e) { OnError?.Invoke("audio device unregister failed", e); }
            _enumerator->Release();
            _enumerator = null;
        }
        // Drop our reference; the audio service released its own on unregister (memory is freed on the last Release).
        if (_volumeCb is not null) Callback.Release(_volumeCb);
        if (_deviceCb is not null) Callback.Release(_deviceCb);
        _volumeCb = null;
        _deviceCb = null;
    }

    private static int ToPercent(float scalar) => (int)Math.Round(Math.Clamp(scalar, 0f, 1f) * 100);

    // ---- hand-built COM callback objects ----

    [StructLayout(LayoutKind.Sequential)]
    private struct Callback
    {
        public void** Vtbl;
        public int Refs;
        public nint Handle; // GCHandle to the AudioVolumeWatcher
        public Guid Iid;    // the interface (besides IUnknown) this object answers to

        public static Callback* Create(AudioVolumeWatcher owner, void** vtbl, Guid iid)
        {
            var p = (Callback*)NativeMemory.AllocZeroed((nuint)sizeof(Callback));
            p->Vtbl = vtbl;
            p->Refs = 1;
            p->Handle = GCHandle.ToIntPtr(GCHandle.Alloc(owner));
            p->Iid = iid;
            return p;
        }

        public static uint Release(Callback* self)
        {
            var n = Interlocked.Decrement(ref self->Refs);
            if (n == 0)
            {
                GCHandle.FromIntPtr(self->Handle).Free();
                NativeMemory.Free(self);
            }
            return (uint)n;
        }

        public static AudioVolumeWatcher? Owner(Callback* self) =>
            GCHandle.FromIntPtr(self->Handle).Target as AudioVolumeWatcher;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(Callback* self, Guid* riid, void** ppv)
    {
        if (*riid == IUnknownIid || *riid == self->Iid)
        {
            Interlocked.Increment(ref self->Refs);
            *ppv = self;
            return SOk;
        }
        *ppv = null;
        return ENoInterface;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(Callback* self) => (uint)Interlocked.Increment(ref self->Refs);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint ReleaseSlot(Callback* self) => Callback.Release(self);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnNotify(Callback* self, AUDIO_VOLUME_NOTIFICATION_DATA* data)
    {
        try { Callback.Owner(self)?.VolumeChanged?.Invoke(ToPercent(data->fMasterVolume), data->bMuted); }
        catch { /* never let an exception cross into the audio engine */ }
        return SOk;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnDefaultDeviceChanged(Callback* self, EDataFlow flow, ERole role, char* deviceId)
    {
        try
        {
            if (flow == EDataFlow.eRender && role == ERole.eConsole) Callback.Owner(self)?.DefaultDeviceChanged?.Invoke();
        }
        catch { }
        return SOk;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int NotifyOk1(Callback* self, void* a) => SOk;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int NotifyOk2(Callback* self, void* a, void* b) => SOk;

    private static readonly void** VolumeVtable = BuildVolumeVtable();
    private static readonly void** DeviceVtable = BuildDeviceVtable();

    private static void** BuildVolumeVtable()
    {
        var v = (void**)NativeMemory.Alloc((nuint)(4 * sizeof(void*)));
        v[0] = (delegate* unmanaged[Stdcall]<Callback*, Guid*, void**, int>)&QueryInterface;
        v[1] = (delegate* unmanaged[Stdcall]<Callback*, uint>)&AddRef;
        v[2] = (delegate* unmanaged[Stdcall]<Callback*, uint>)&ReleaseSlot;
        v[3] = (delegate* unmanaged[Stdcall]<Callback*, AUDIO_VOLUME_NOTIFICATION_DATA*, int>)&OnNotify;
        return v;
    }

    private static void** BuildDeviceVtable()
    {
        var v = (void**)NativeMemory.Alloc((nuint)(8 * sizeof(void*)));
        v[0] = (delegate* unmanaged[Stdcall]<Callback*, Guid*, void**, int>)&QueryInterface;
        v[1] = (delegate* unmanaged[Stdcall]<Callback*, uint>)&AddRef;
        v[2] = (delegate* unmanaged[Stdcall]<Callback*, uint>)&ReleaseSlot;
        v[3] = (delegate* unmanaged[Stdcall]<Callback*, void*, void*, int>)&NotifyOk2;   // OnDeviceStateChanged(id, state)
        v[4] = (delegate* unmanaged[Stdcall]<Callback*, void*, int>)&NotifyOk1;          // OnDeviceAdded(id)
        v[5] = (delegate* unmanaged[Stdcall]<Callback*, void*, int>)&NotifyOk1;          // OnDeviceRemoved(id)
        v[6] = (delegate* unmanaged[Stdcall]<Callback*, EDataFlow, ERole, char*, int>)&OnDefaultDeviceChanged;
        v[7] = (delegate* unmanaged[Stdcall]<Callback*, void*, void*, int>)&NotifyOk2;   // OnPropertyValueChanged(id, key)
        return v;
    }
}
