using System.Buffers;
using System.Diagnostics;
using OpenTK.Audio.OpenAL;

namespace Playful.Common.Player;

public sealed class MPlayerOutput : IDisposable
{
    private const int BufferSizeInSamples = 8 * 1024;
    private const double PreBufferInSeconds = 0.5;
    private const double MaxBufferInSeconds = 10;

    public string? Debug => GetDebugText();

    public PlayState PlayState
    {
        get => _playState;
    }

    public PlayState GetPlayState()
    {
        EnsureNotDisposed();
        int sta;
        if (_source == 0)
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
        int source = _source;
        AL.GetSource(source, ALGetSourcei.SourceState, out sta);
        Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.GetSource)} ({ceSource})", source);
        var state = (ALSourceState)sta switch
        {
            ALSourceState.Initial => PlayState.Initial,
            ALSourceState.Playing => PlayState.Playing,
            ALSourceState.Paused => PlayState.Paused,
            ALSourceState.Stopped => PlayState.Stopped,
            _ => PlayState.Unknown
        };
        return state;
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
            return _baseSample + _processedSamples + _sampleInBuffer;
        }
        _areSource.WaitOne();
        try
        {
            int source = _source;
            AL.GetSource(source, ALGetSourcei.SampleOffset, out int sample);
            Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.GetSource)} ({ceSource})", source);
            return _baseSample + _processedSamples + (_sampleInBuffer = sample);
        }
        finally
        {
            _areSource.Set();
        }
    }

    private string GetDebugText()
    {
        //int queuedSamples = _bufferToSampleCount.Values.Sum();
        //int preferredSampleQueueSize = (int)(PreBufferInSeconds * _sampleRate);
        //return $"n{_bufferToSampleCount.Count:X04}q{queuedSamples:X06}x{preferredSampleQueueSize:X06}{_generator.GetDebugText()}";
        return $"n{_bufferToSampleCount.Count:X04}{_generator.GetDebugText()} {_fillSamplesPerMillisecond:F3}";
    }

    public double Duration => GetTimeFromSample(Length);

    public int Length { get; }

    private bool _sameDesu;
    private int _processedSamples;
    private int _sampleInBuffer;

    private readonly SoundGenerator _generator;
    private readonly int _sampleRate;
    private readonly TextWriter? _debug;
    private readonly Dictionary<int, int> _bufferToSampleCount;
    private readonly AutoResetEvent _areSource;
    private readonly AutoResetEvent _areBuffer;
    private int _baseSample;
    private bool _disposed;
    private int _source;
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

    public MPlayerOutput(SoundGenerator generator, TextWriter? debug = null)
    {
        _generator = generator;
        _sampleRate = _generator.Frequency;
        Length = _generator.Length;
        _bufferToSampleCount = new Dictionary<int, int>();
        _areSource = new AutoResetEvent(true);
        _areBuffer = new AutoResetEvent(true);
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
        _baseSample = sample;
        _processedSamples = 0;
        _sampleInBuffer = 0;
        _generator.Reset(sample);
        CancellationTokenSource cts = new();
        Task streamData = StreamData(cts.Token);
        return new ActiveSession(streamData, cts);
    }

    private void ResetStreamData(int sample)
    {
        _baseSample = sample;
        _processedSamples = 0;
        _sampleInBuffer = 0;
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
        (int numChannels, ALFormat format) = generator.Format switch
        {
            AudioFormat.Pcm8X1 => (1, ALFormat.Mono8),
            AudioFormat.Pcm8X2 => (2, ALFormat.Stereo8),
            AudioFormat.Pcm16X1 => (1, ALFormat.Mono16),
            AudioFormat.Pcm16X2 => (2, ALFormat.Stereo16),
            _ => throw new ArgumentOutOfRangeException()
        };
        int elementCount = wantedSamples * numChannels;
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
            ReadOnlyMemory<TSample> data = dataTmp.AsMemory(0, samples * numChannels);
            int buf = AL.GenBuffer();
            Ce(static _ => $"{nameof(AL)}.{nameof(AL.GenBuffer)}");
            AL.BufferData(buf, format, data.Span, _sampleRate);
            Ce(static _ => $"{nameof(AL)}.{nameof(AL.BufferData)}");
            cancellationToken.ThrowIfCancellationRequested();
            _areSource.WaitOne();
            try
            {
                int source = _source;
                AL.SourceQueueBuffer(source, buf);
                Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.SourceQueueBuffer)} ({ceSource})");
                _sameDesu = true;
            }
            finally
            {
                _areSource.Set();
            }
            _bufferToSampleCount[buf] = samples;
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
        int source;
        ClearError();
        _source = AL.GenSource();
        Ce(static _ => $"{nameof(AL)}.{nameof(AL.GenSource)}");
        _running = true;
        try
        {
            while (true)
            {
                ClearError();
                UpdatePlayData();
                cancellationToken.ThrowIfCancellationRequested();
                // TODO make this vary as play progresses
                int preferredSampleQueueSize = (int)(PreBufferInSeconds * _sampleRate);
                int queuedSamples = _bufferToSampleCount.Values.Sum();
                int remainingSamplesToAdd = Math.Max(0, preferredSampleQueueSize - queuedSamples);
                int samplesFilled = 0;
                Stopwatch sw = new();
                while (remainingSamplesToAdd > 0)
                {
                    sw.Restart();
                    int samples = Queue(Math.Min(BufferSizeInSamples, remainingSamplesToAdd), cancellationToken);
                    TimeSpan ts = sw.Elapsed;
                    _fillSamplesPerMillisecond = samples / ts.TotalMilliseconds;
                    UpdatePlayData();
                    cancellationToken.ThrowIfCancellationRequested();
                    remainingSamplesToAdd -= samples;
                    if (samples <= 0)
                    {
                        break;
                    }
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
                    source = _source;
                    AL.SourcePlay(source);
                    Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.SourcePlay)} ({ceSource})", source);
                }
                else
                {
                    if (PlayState != PlayState.Playing)
                    {
                        source = _source;
                        AL.SourceStop(source);
                        Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.SourceStop)} ({ceSource})", source);
                        AL.SourcePlay(source);
                        Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.SourcePlay)} ({ceSource})", source);
                    }
                }
                // Wait for at least one buffer to finish processing
                _areBuffer.WaitOne();
                try
                {
                    if (_sameDesu)
                    {
                        _sameDesu = false;
                        int runs = 100;
                        while (runs-- > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            source = _source;
                            ClearError();
                            AL.GetSource(source, ALGetSourcei.BuffersProcessed, out int _);
                            Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.GetSource)} ({ceSource})", source);
                            int iterationProcessedSamples = await CleanupBuffersAsync(source, cancellationToken);
                            if (iterationProcessedSamples > 0)
                            {
                                break;
                            }
                        }
                    }
                }
                finally
                {
                    _areBuffer.Set();
                }
            }
            // Wait for all buffers to finish processing
            _areBuffer.WaitOne();
            try
            {
                while ((_playState = GetPlayState()) == PlayState.Playing)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    source = _source;
                    AL.GetSource(source, ALGetSourcei.BuffersQueued, out int queued);
                    Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.GetSource)} ({ceSource})", source);
                    AL.GetSource(source, ALGetSourcei.BuffersProcessed, out int processed);
                    Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.GetSource)} ({ceSource})", source);
                    if (queued == 0 && processed == 0)
                    {
                        break;
                    }
                    await CleanupBuffersAsync(source, cancellationToken);
                }
            }
            finally
            {
                _areBuffer.Set();
            }
            _sampleInBuffer = 0;
            _ended = true;
            _running = false;
            source = _source;
            AL.SourceStop(source);
            Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.SourceStop)} ({ceSource})", source);
            UpdatePlayData();
        }
        catch (OperationCanceledException)
        {
            _running = false;
            source = _source;
            if (AL.IsSource(source))
            {
                AL.GetSource(source, ALGetSourcei.SampleOffset, out int sample);
                Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.GetSource)} ({ceSource})", source);
                _sampleInBuffer = sample;
                AL.SourceStop(source);
                Ce(static _ => $"{nameof(AL)}.{nameof(AL.SourceStop)}", source);
                UpdatePlayData();
            }
            throw;
        }
        finally
        {
            source = _source;
            _source = 0;
            AL.DeleteSource(source);
            Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.DeleteSource)} ({ceSource})", source);
            foreach (var bb in _bufferToSampleCount.Keys)
            {
                AL.DeleteBuffer(bb);
                Ce(static _ => $"{nameof(AL)}.{nameof(AL.DeleteBuffer)}");
            }
            _bufferToSampleCount.Clear();
        }
    }

    private async Task<int> CleanupBuffersAsync(int source, CancellationToken cancellationToken)
    {
        ClearError();
        AL.GetSource(source, ALGetSourcei.BuffersProcessed, out int processed);
        Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.GetSource)} ({ceSource})", source);
        if (processed <= 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        else
        {
            int iterationProcessedSamples = 0;
            while (processed-- > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                source = _source;
                int bb = AL.SourceUnqueueBuffer(source);
                Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.SourceUnqueueBuffer)} ({ceSource})", source);
                int bufferSampleCount = _bufferToSampleCount[bb];
                _processedSamples += bufferSampleCount;
                iterationProcessedSamples += bufferSampleCount;
                AL.DeleteBuffer(bb);
                Ce(static _ => $"{nameof(AL)}.{nameof(AL.DeleteBuffer)}", source);
                _bufferToSampleCount.Remove(bb);
                UpdatePlayData();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return iterationProcessedSamples;
        }
        return 0;
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MPlayerOutput));
        }
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

    private static void ClearError()
    {
        AL.GetError();
    }

    private static void Ce(Func<int?, string> op, int? source = null)
    {
        ALError error = AL.GetError();
        if (error == ALError.NoError)
        {
            return;
        }
        string e = AL.GetErrorString(error);
        string err = $"{op(source)}::{error}: {e} {(source is { } sourceV ? AL.IsSource(sourceV) : "xx")}";
        throw new InvalidOperationException(err);
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
        _sameDesu = false;
    }

    private void ReleaseUnmanagedResources()
    {
        DestroyCurrentTask();
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        _disposed = true;
        _areSource.Dispose();
        _areBuffer.Dispose();
        GC.SuppressFinalize(this);
    }

    ~MPlayerOutput() => ReleaseUnmanagedResources();
}
