using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.System.Com;

namespace NotWin.Native;

/// <summary>
/// Peak level of the default render endpoint (IAudioMeterInformation), read on demand: the notch's equalizer samples it
/// only while the music state is on screen. Bound per use; <see cref="Peak"/> returns null when there is no device or
/// the device went away (call <see cref="Bind"/> again). UI thread only. Vtable calls as in <see cref="AudioVolumeWatcher"/>
/// (IMMDeviceEnumerator 4 GetDefaultAudioEndpoint; IMMDevice 3 Activate; IAudioMeterInformation 3 GetPeakValue).
/// </summary>
public sealed unsafe class AudioPeakMeter : IDisposable
{
    private static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid MeterIid = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    private void* _meter;

    public bool Bound => _meter is not null;

    /// <summary>(Re)attaches to the current default render device. False when there is none.</summary>
    public bool Bind()
    {
        Unbind();
        IMMDeviceEnumerator* enumerator = null;
        IMMDevice* device = null;
        try
        {
            var clsid = MMDeviceEnumeratorClsid;
            var iid = IMMDeviceEnumerator.IID_Guid;
            void* raw;
            if (PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, &raw).Failed) return false;
            enumerator = (IMMDeviceEnumerator*)raw;
            var hr = ((delegate* unmanaged[Stdcall]<IMMDeviceEnumerator*, EDataFlow, ERole, IMMDevice**, HRESULT>)(*(void***)enumerator)[4])(enumerator, EDataFlow.eRender, ERole.eConsole, &device);
            if (hr.Failed || device is null) return false;
            var meterIid = MeterIid;
            void* meter = null;
            hr = ((delegate* unmanaged[Stdcall]<IMMDevice*, Guid*, CLSCTX, void*, void**, HRESULT>)(*(void***)device)[3])(device, &meterIid, CLSCTX.CLSCTX_ALL, null, &meter);
            if (hr.Failed || meter is null) return false;
            _meter = meter;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (device is not null) device->Release();
            if (enumerator is not null) enumerator->Release();
        }
    }

    /// <summary>Current peak (0..1), or null when unbound or the device was invalidated (then unbound).</summary>
    public float? Peak()
    {
        if (_meter is null) return null;
        float peak = 0;
        var hr = ((delegate* unmanaged[Stdcall]<void*, float*, HRESULT>)(*(void***)_meter)[3])(_meter, &peak);
        if (hr.Succeeded) return peak;
        Unbind();
        return null;
    }

    public void Unbind()
    {
        if (_meter is null) return;
        ((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)_meter)[2])(_meter); // IUnknown::Release
        _meter = null;
    }

    public void Dispose() => Unbind();
}
