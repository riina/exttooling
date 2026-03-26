using System.Buffers;
using System.Diagnostics;

namespace Playful.Player;

public sealed class MPlayerOutput : IDisposable
{
    private const int BufferSizeInSamples = 8 * 1024;
    private const double PreBufferInSeconds = 0.5;
    private const double MaxBufferInSeconds = 10;

    public string Debug => GetDebugText();

    public PlayState PlayState
    {
        get => _playState;
    }

    private PlayState GetPlayState()
    {
        EnsureNotDisposed();
        if (_backend == null)
        {
            return PlayState.Stopped;
        }
        if (_ended)
        {
            return PlayState.Ended;
        }
        if (!_running)
        {
            return PlayState.Stopped;
        }
        return _backend.GetPlayState();
    }

    public double TimeApprox => GetTimeFromSample(Sample);
    public double TimeCacheStart => GetTimeFromSample(SampleCacheStart);
    public double TimeCacheEnd => GetTimeFromSample(SampleCacheEnd);

    public int Sample
    {
        get => _sample;
    }

    public int SampleCacheStart
    {
        get => _sampleCacheStart;
    }

    public int SampleCacheEnd
    {
        get => _sampleCacheEnd;
    }

    private int GetSample()
    {
        EnsureNotDisposed();
        if (!_running)
        {
            return _baseSample + _cachedSampleOffset;
        }
        return _baseSample + (_cachedSampleOffset = _backend?.GetSampleOffset() ?? 0);
    }

    private string GetDebugText()
    {
        return $"{_generator.GetDebugText()} {_fillSamplesPerMillisecond:F3}";
    }

    public double Duration => GetTimeFromSample(Length);

    public int Length { get; }

    private int _cachedSampleOffset;

    private readonly object __backendLock = new();
    private readonly MPlayerBackendCreationDelegate _backendCreationDelegate;
    private readonly SoundGenerator _generator;
    private readonly int _sampleRate;
    private readonly int _numChannels;
    private readonly TextWriter? _debug;
    private IPlayerBackend? _backend;
    private int _baseSample;
    private bool _disposed;
    private ActiveSession? _activeSession;
    private bool _ended;
    private int _sample;
    private int _sampleCacheStart;
    private int _sampleCacheEnd;
    private PlayState _playState;
    private bool _running;
    private double _fillSamplesPerMillisecond;

    private record ActiveSession(Task Task, CancellationTokenSource Cts)
    {
        public void Stop() => Cts.Cancel();
    }

    public MPlayerOutput(SoundGenerator generator, MPlayerBackendCreationDelegate backendCreationDelegate, TextWriter? debug = null)
    {
        _generator = generator;
        _backendCreationDelegate = backendCreationDelegate;
        _numChannels = generator.Format switch
        {
            AudioFormat.Pcm8X1 => 1,
            AudioFormat.Pcm8X2 => 2,
            AudioFormat.Pcm16X1 => 1,
            AudioFormat.Pcm16X2 => 2,
            _ => throw new ArgumentOutOfRangeException()
        };
        _sampleRate = _generator.Frequency;
        Length = _generator.Length;
        _debug = debug;
    }

    public Task PlayAsync(double time = 0, CancellationToken cancellationToken = default)
    {
        return PlayInternal(GetSampleFromTime(time), cancellationToken);
    }

    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        return PlayInternal(Sample, cancellationToken);
    }

    private async Task PlayInternal(int sample, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        sample = ClampSample(sample);
        var previousPlayState = _playState;
        DestroyCurrentTask();
        if (sample >= Length && _sample < Length)
        {
            UpdatePlayDataAsEnd(previousPlayState);
            _ended = true;
            return;
        }
        _ended = false;
        _activeSession = StartStreamData(ClampSample(sample));
        while (true)
        {
            if (_activeSession.Task.IsCompleted)
            {
                await _activeSession.Task;
                return;
            }
            PlayState ps = PlayState;
            switch (ps)
            {
                case PlayState.Playing:
                    return;
                case PlayState.Initial:
                case PlayState.Paused:
                case PlayState.Stopped:
                case PlayState.Unknown:
                    await Task.Delay(10, cancellationToken);
                    break;
            }
        }
    }

    private void Seek(double time)
    {
        SeekInternal(GetSampleFromTime(time));
    }

    private void SeekInternal(int sample)
    {
        EnsureNotDisposed();
        sample = ClampSample(sample);
        var previousPlayState = _playState;
        DestroyCurrentTask();
        if (sample >= Length)
        {
            UpdatePlayDataAsEnd(previousPlayState);
            _ended = true;
        }
        _ended = false;
        ResetStreamData(sample);
        UpdatePlayDataForSeek(sample);
    }

    public Task PlaySeekAsync(double deltaTime = 0, CancellationToken cancellationToken = default)
    {
        return PlayAsync(TimeApprox + deltaTime, cancellationToken);
    }

    public Task SeekMaintainStateAsync(double deltaTime = 0, CancellationToken cancellationToken = default)
    {
        PlayState ps = PlayState;
        double targetTime = TimeApprox + deltaTime;
        if (ps == PlayState.Playing)
        {
            return PlayAsync(targetTime, cancellationToken);
        }
        Seek(targetTime);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        EnsureNotDisposed();
        DestroyCurrentTask();
    }

    private int ClampSample(int sample) => Math.Clamp(sample, 0, Length);

    private int GetSampleFromTime(double time) => (int)(time * _sampleRate);

    private double GetTimeFromSample(int sample) => sample / (double)_sampleRate;

    private ActiveSession StartStreamData(int sample)
    {
        ResetStreamData(sample);
        CancellationTokenSource cts = new();
        Task streamData = StreamData(cts.Token);
        return new ActiveSession(streamData, cts);
    }

    private void ResetStreamData(int sample)
    {
        _baseSample = sample;
        _cachedSampleOffset = 0;
        _generator.Reset(sample);
    }

    public Task GetPlayTask()
    {
        EnsureNotDisposed();
        return GetPlayTaskResetIfComplete();
    }

    private int Queue(int wantedSamples, CancellationToken cancellationToken = default)
    {
        return _generator switch
        {
            SoundGenerator<byte> s => Queue(s, wantedSamples, cancellationToken),
            SoundGenerator<sbyte> s => Queue(s, wantedSamples, cancellationToken),
            SoundGenerator<short> s => Queue(s, wantedSamples, cancellationToken),
            SoundGenerator<ushort> s => Queue(s, wantedSamples, cancellationToken),
            _ => throw new ArgumentException()
        };
    }

    private int Queue<TSample>(SoundGenerator<TSample> generator, int wantedSamples, CancellationToken cancellationToken = default) where TSample : unmanaged
    {
        cancellationToken.ThrowIfCancellationRequested();
        int elementCount = wantedSamples * _numChannels;
        TSample[] dataTmp = ArrayPool<TSample>.Shared.Rent(elementCount);
        int samples;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _debug?.Write("Waiting for buffer... ");
            samples = generator.FillBuffer(wantedSamples, dataTmp.AsMemory(0, elementCount), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _debug?.WriteLine($"{samples} samples");
            if (samples <= 0)
            {
                return 0;
            }
            ReadOnlyMemory<TSample> data = dataTmp.AsMemory(0, samples * _numChannels);
            _backend?.QueueBuffer(data);
        }
        finally
        {
            ArrayPool<TSample>.Shared.Return(dataTmp);
        }
        return samples;
    }

    private void UpdatePlayDataAsEnd(PlayState previousPlayState)
    {
        _playState = previousPlayState == PlayState.Playing ? PlayState.Ended : _playState;
        _sample = Length;
        var sampleRange = _generator.GetSurroundingCachedSampleRange(Math.Max(0, Length - 1));
        _sampleCacheStart = sampleRange.Start.GetOffset(Length);
        _sampleCacheEnd = sampleRange.End.GetOffset(Length);
    }

    private void UpdatePlayData()
    {
        _playState = GetPlayState();
        _sample = GetSample();
        var sampleRange = _generator.GetSurroundingCachedSampleRange(_sample);
        _sampleCacheStart = sampleRange.Start.GetOffset(Length);
        _sampleCacheEnd = sampleRange.End.GetOffset(Length);
    }

    private void UpdatePlayDataForSeek(int sample)
    {
        _sample = sample;
        var sampleRange = _generator.GetSurroundingCachedSampleRange(_sample);
        _sampleCacheStart = sampleRange.Start.GetOffset(Length);
        _sampleCacheEnd = sampleRange.End.GetOffset(Length);
    }

    private async Task StreamData(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        await Task.Yield();
        double preBufferLeft = PreBufferInSeconds;
        IPlayerBackend backend;
        lock (__backendLock)
        {
            backend = _backendCreationDelegate(_generator.Format, _sampleRate);
            _backend = backend;
        }
        _running = true;
        try
        {
            while (true)
            {
                UpdatePlayData();
                cancellationToken.ThrowIfCancellationRequested();
                int preferredSampleQueueSize = (int)(PreBufferInSeconds * _sampleRate);
                int maxSampleQueueSize = (int)(MaxBufferInSeconds * _sampleRate);
                int queuedSamples = backend.GetQueuedSamples();
                int remainingSamplesToAdd;
                if (queuedSamples < maxSampleQueueSize)
                {
                    double fillSamplesPerMillisecond = _fillSamplesPerMillisecond;
                    if (fillSamplesPerMillisecond <= 0)
                    {
                        remainingSamplesToAdd = queuedSamples < preferredSampleQueueSize ? preferredSampleQueueSize - queuedSamples : 0;
                    }
                    else
                    {
                        int sampleBudget = (int)(10 * fillSamplesPerMillisecond);
                        remainingSamplesToAdd = Math.Min(maxSampleQueueSize - queuedSamples, sampleBudget);
                    }
                }
                else
                {
                    remainingSamplesToAdd = 0;
                }
                if (remainingSamplesToAdd > 0)
                {
                    int samplesFilled = 0;
                    Stopwatch sw = new();
                    while (remainingSamplesToAdd > 0)
                    {
                        sw.Restart();
                        int samples = Queue(Math.Min(BufferSizeInSamples, remainingSamplesToAdd), cancellationToken);
                        TimeSpan ts = sw.Elapsed;
                        UpdatePlayData();
                        if (samples <= 0)
                        {
                            break;
                        }
                        _fillSamplesPerMillisecond = samples / ts.TotalMilliseconds;
                        cancellationToken.ThrowIfCancellationRequested();
                        remainingSamplesToAdd -= samples;
                        samplesFilled += samples;
                    }
                    if (samplesFilled <= 0)
                    {
                        break;
                    }
                    if (preBufferLeft > 0)
                    {
                        preBufferLeft -= samplesFilled / (double)_sampleRate;
                        if (preBufferLeft > 0)
                        {
                            continue;
                        }
                        backend.Play(restart: false);
                    }
                    else
                    {
                        if (PlayState != PlayState.Playing)
                        {
                            backend.Play(restart: true);
                        }
                    }
                }
                await backend.WaitForNextLoopAsync(UpdatePlayData, cancellationToken);
                UpdatePlayData();
            }
            await backend.WaitForFinishAsync(UpdatePlayData, cancellationToken);
            UpdatePlayData();
            _ended = true;
            _running = false;
            backend.Stop();
            UpdatePlayData();
        }
        catch (OperationCanceledException)
        {
            _running = false;
            UpdatePlayData();
            backend.Stop();
            UpdatePlayData();
            throw;
        }
        finally
        {
            lock (__backendLock)
            {
                _backend = null;
                backend.Dispose();
            }
        }
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(MPlayerOutput));
    }

    private Task GetPlayTaskResetIfComplete()
    {
        if (_activeSession != null)
        {
            if (_activeSession.Task.IsCompleted)
            {
                DestroyCurrentTask();
                return Task.CompletedTask;
            }
            return _activeSession.Task;
        }
        return Task.CompletedTask;
    }

    private void DestroyCurrentTask()
    {
        if (_activeSession == null)
        {
            return;
        }
        _activeSession.Stop();
        try
        {
            _activeSession.Task.Wait();
        }
        catch (AggregateException)
        {
            // ignored
        }
        _activeSession = null;
    }

    private void ReleaseUnmanagedResources()
    {
        DestroyCurrentTask();
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        _disposed = true;
        lock (__backendLock)
        {
            var backend = _backend;
            if (backend != null)
            {
                _backend = null;
                backend.Dispose();
            }
        }
        GC.SuppressFinalize(this);
    }

    ~MPlayerOutput() => ReleaseUnmanagedResources();
}
