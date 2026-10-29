using System.Collections.Concurrent;
using System.Windows.Threading;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using ViggiSlideshow.Core;

namespace ViggiSlideshow;

/// <summary>
/// A video played by Windows' Media Foundation, which decodes on the graphics card and also
/// shrinks each frame to the screen there, so only a screen-sized frame is copied back to memory.
/// VLC 3 can only shrink frames on the CPU, after copying the whole frame back, which held 5.3K
/// GoPro videos to about 22 of 30 fps; this plays them at full rate. Used for videos bigger than
/// 4K; one Media Foundation can't start goes back to VLC.
///
/// Everything that touches Media Foundation or Direct3D runs on one worker thread of its own, so
/// a slow call can never stall the UI thread. The UI thread only queues commands for it.
/// </summary>
internal sealed class GpuVideoPlayback : VideoPlayback
{
    // Media Foundation's "no frame yet" for TransferVideoFrame, until the first frame is decoded
    private const int NoVideoSampleAvailable = unchecked((int)0xC00D4E26);

    private static readonly Lazy<bool> Started = new(() =>
    {
        MediaFactory.MFStartup(true).CheckError();
        return true;
    });

    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action<IMFMediaEngine>> _commands = new();
    private readonly ConcurrentQueue<(MediaEngineEvent Event, nuint Param1, int Param2)> _events = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MediaEngineNotifyDelegate _notify;
    private readonly bool _mute;
    private volatile bool _stopping;

    // Worker thread only
    private ID3D11Device? _device;
    private ID3D11Texture2D? _target, _staging;
    private int _sourceWidth, _sourceHeight, _width, _height, _pitch;
    private long _lastPts = -1, _shortestFrame = long.MaxValue;

    public GpuVideoPlayback(string path, int maxWidth, int maxHeight, bool mute, Dispatcher ui)
        : base(path, maxWidth, maxHeight, ui)
    {
        _mute = mute;
        _notify = (e, param1, param2) =>
        {
            _events.Enqueue((e, param1, param2));
            _wake.Set();
        };
        _thread = new Thread(Run) { IsBackground = true, Name = "Media Foundation video" };
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    protected override void Play() => _thread.Start();

    public override void SetMute(bool mute) => Send(engine => engine.Muted = mute);

    public override void SetPaused(bool paused) => Send(engine =>
    {
        if (paused)
            engine.Pause();
        else
            engine.Play();
    });

    protected override Task Close()
    {
        _stopping = true;
        _wake.Set();
        if (!_thread.IsAlive && _thread.ThreadState.HasFlag(ThreadState.Unstarted))
            _closed.TrySetResult();
        return _closed.Task;
    }

    // Media Foundation doesn't count its own frames for us
    protected override (string Player, long? Decoded, long? Shown, long? Lost) PlayerStats() =>
        ("Media Foundation, scaled on the graphics card", null, null, null);

    private void Send(Action<IMFMediaEngine> command)
    {
        if (IsDisposed)
            return;
        _commands.Enqueue(command);
        _wake.Set();
    }

    // ---- Worker thread ----

    private void Run()
    {
        IMFDXGIDeviceManager? manager = null;
        IMFMediaEngine? engine = null;
        try
        {
            _ = Started.Value;
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out _device).CheckError();
            // Media Foundation decodes on the same device from threads of its own
            using (var multithread = _device!.QueryInterface<ID3D11Multithread>())
                multithread.SetMultithreadProtected(true);
            manager = MediaFactory.MFCreateDXGIDeviceManager();
            manager.ResetDevice(_device).CheckError();

            using (var factory = new IMFMediaEngineClassFactory())
            using (var attributes = MediaFactory.MFCreateAttributes(2))
            {
                attributes.Set(MediaEngineAttributeKeys.DxgiManager.Guid, manager);
                attributes.Set(MediaEngineAttributeKeys.VideoOutputFormat.Guid, (uint)Format.B8G8R8A8_UNorm);
                engine = factory.CreateInstance(MediaEngineCreateFlags.None, attributes, _notify);
            }
            engine.Muted = _mute;
            engine.SetSource(Path);
            engine.Play();

            while (!_stopping)
            {
                while (!_stopping && _commands.TryDequeue(out var command))
                    command(engine);
                while (!_stopping && _events.TryDequeue(out var e))
                    OnEvent(engine, e.Event, e.Param1, e.Param2);
                if (!_stopping && _target is not null)
                    TakeFrame(engine);
                _wake.WaitOne(4);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Media Foundation playing {Path}", e);
            RaiseFailed($"Media Foundation couldn't play it: {e.Message}");
        }
        finally
        {
            try
            {
                engine?.Shutdown();
                engine?.Dispose();
                manager?.Dispose();
                _staging?.Dispose();
                _target?.Dispose();
                _device?.Dispose();
            }
            catch (Exception e)
            {
                Log.Error($"Closing video {Path}", e);
            }
            _closed.TrySetResult();
        }
    }

    private void OnEvent(IMFMediaEngine engine, MediaEngineEvent e, nuint param1, int param2)
    {
        switch (e)
        {
            // The size at LoadedMetadata can still be the decoder's padded one (5312x2992 for
            // 5312x2988), which would letterbox the picture; by the first frame it's the real one
            case MediaEngineEvent.FirstFrameReady:
            case MediaEngineEvent.FormatChange when _target is not null:
                engine.GetNativeVideoSize(out int width, out int height);
                if (width <= 0 || height <= 0)
                {
                    RaiseFailed("Media Foundation found no picture in it");
                    return;
                }
                if (width == _sourceWidth && height == _sourceHeight)
                    return;
                CreateTextures(width, height);
                engine.GetVideoAspectRatio(out int aspectX, out int aspectY);
                double duration = engine.Duration;
                var details = new VideoDetails(width, height, PixelAspect(width, height, aspectX, aspectY), 0,
                    double.IsFinite(duration) ? (long)(duration * 1000) : 0);
                Post(() => RaiseDetails(details));
                break;
            case MediaEngineEvent.Ended:
                RaiseEnded();
                break;
            case MediaEngineEvent.Error:
                RaiseFailed($"Media Foundation couldn't play it (error {param1}, 0x{param2:X8})");
                break;
        }
    }

    private void CreateTextures(int sourceWidth, int sourceHeight)
    {
        (_sourceWidth, _sourceHeight) = (sourceWidth, sourceHeight);
        (_width, _height, _pitch, _) = SetFrameSize(sourceWidth, sourceHeight);
        _target?.Dispose();
        _staging?.Dispose();
        _target = _device!.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)_width, (uint)_height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource));
        _staging = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)_width, (uint)_height, 1, 1,
            BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
    }

    /// <summary>Copies the frame due now, shrunk to the screen by the graphics card, into the write buffer.</summary>
    private void TakeFrame(IMFMediaEngine engine)
    {
        if (!engine.OnVideoStreamTick(out long pts) || pts == _lastPts)
            return;

        try
        {
            engine.TransferVideoFrame(_target!, null, new Vortice.RawRect(0, 0, _width, _height), null);
        }
        catch (SharpGenException e) when (e.ResultCode.Code == NoVideoSampleAvailable)
        {
            return;
        }

        ID3D11DeviceContext context = _device!.ImmediateContext;
        context.CopyResource(_staging!, _target!);
        MappedSubresource map = context.Map(_staging!, 0, MapMode.Read);
        try
        {
            int rowBytes = _width * 4;
            unsafe
            {
                byte* source = (byte*)map.DataPointer;
                byte* target = (byte*)WriteBuffer;
                for (int row = 0; row < _height; row++)
                    Buffer.MemoryCopy(source + (long)row * map.RowPitch, target + (long)row * _pitch, rowBytes, rowBytes);
            }
        }
        finally
        {
            context.Unmap(_staging!, 0);
        }
        FrameWritten();

        // Dropped frames only make the gaps longer, so the shortest gap is the source's frame time
        if (_lastPts >= 0 && pts > _lastPts)
        {
            _shortestFrame = Math.Min(_shortestFrame, pts - _lastPts);
            SourceFps = 10_000_000.0 / _shortestFrame;
        }
        _lastPts = pts;
        RaiseProgress(pts / 10_000);
    }

    /// <summary>Width of a pixel relative to its height, from the picture's aspect ratio; 1 if that's unknown.</summary>
    private static double PixelAspect(int width, int height, int aspectX, int aspectY)
    {
        if (aspectX <= 0 || aspectY <= 0)
            return 1.0;
        double aspect = (double)aspectX / aspectY / ((double)width / height);
        return Math.Abs(aspect - 1.0) < 0.01 ? 1.0 : aspect;
    }
}
