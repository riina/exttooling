using System.Buffers;
using OpenTK.Audio.OpenAL;

namespace Playful;

public sealed class MPlayerOutput : IDisposable
{
    private const int BufferSizeInSamples = 8 * 1024;
    private const double PreBufferInSeconds = 0.5;

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
        if (_ended) return PlayState.Ended;
        if (!_running) return PlayState.Stopped;
        int source = _source;
        AL.GetSource(source, ALGetSourcei.SourceState, out sta);
        Ce($"{nameof(AL)}.{nameof(AL.GetSource)} ({source})");
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

    public int Sample
    {
        get => _sample;
    }

    private int GetSample()
    {
        EnsureNotDisposed();
        if (!_running) return _baseSample + _processedSamples + _sampleInBuffer;
        _areSource.WaitOne();
        try
        {
            int source = _source;
            AL.GetSource(source, ALGetSourcei.SampleOffset, out int sample);
            Ce($"{nameof(AL)}.{nameof(AL.GetSource)} ({source})");
            return _baseSample + _processedSamples + (_sampleInBuffer = sample);
        }
        finally
        {
            _areSource.Set();
        }
    }

    public double Duration => GetTimeFromSample(Length);

    public int Length { get; }

    private bool _sameDesu;
    private int _processedSamples;
    private int _sampleInBuffer;

    private readonly SoundGenerator _generator;
    private readonly int _sampleRate;
    private readonly TextWriter? _debug;
    private readonly Dictionary<int, int> _sampleSizes;
    private readonly AutoResetEvent _areSource;
    private readonly AutoResetEvent _areBuffer;
    private int _baseSample;
    private bool _disposed;
    private int _source;
    private ActiveSession? _activeSession;
    private bool _ended;
    private int _sample;
    private PlayState _playState;
    private bool _running;

    private record ActiveSession(Task Task, CancellationTokenSource Cts)
    {
        public void Stop() => Cts.Cancel();
    }

    public MPlayerOutput(SoundGenerator generator, TextWriter? debug = null)
    {
        _generator = generator;
        _sampleRate = _generator.Frequency;
        Length = _generator.Length;
        _sampleSizes = new Dictionary<int, int>();
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
        DestroyCurrentTask();
        if (sample + _sampleRate * 1 >= Length)
        {
            _ended = true;
            return;
        }
        _ended = false;
        _activeSession = StartStreamData(ClampSample(sample));
        while (true)
        {
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

    public Task PlaySeekAsync(double deltaTime = 0, CancellationToken cancellationToken = default)
    {
        return PlayAsync(TimeApprox + deltaTime, cancellationToken);
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
            if (_debug != null) _debug.Write("Waiting for buffer... ");
            samples = generator.FillBuffer(wantedSamples, dataTmp.AsMemory(0, elementCount), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            ArrayPool<TSample>.Shared.Return(dataTmp);
        }
        if (_debug != null) _debug.WriteLine($"{samples} samples");
        if (samples <= 0) return 0;
        Memory<TSample> data = dataTmp.AsMemory(0, samples * numChannels);
        int buf = AL.GenBuffer();
        Ce($"{nameof(AL)}.{nameof(AL.GenBuffer)}");
        AL.BufferData<TSample>(buf, format, data.Span, _sampleRate);
        Ce($"{nameof(AL)}.{nameof(AL.BufferData)}");
        cancellationToken.ThrowIfCancellationRequested();
        _areSource.WaitOne();
        try
        {
            int source = _source;
            AL.SourceQueueBuffer(source, buf);
            Ce($"{nameof(AL)}.{nameof(AL.SourceQueueBuffer)} ({source})");
            _sameDesu = true;
        }
        finally
        {
            _areSource.Set();
        }
        _sampleSizes[buf] = samples;
        return samples;
    }

    private async Task StreamData(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        await Task.Yield();
        double preBufferLeft = PreBufferInSeconds;
        int source;
        ClearError();
        _source = AL.GenSource();
        Ce($"{nameof(AL)}.{nameof(AL.GenSource)}");
        _running = true;
        try
        {
            while (true)
            {
                ClearError();
                _playState = GetPlayState();
                _sample = GetSample();
                cancellationToken.ThrowIfCancellationRequested();
                int samplesFilled = 0;
                while (samplesFilled < BufferSizeInSamples)
                {
                    int samples = Queue(BufferSizeInSamples - samplesFilled, cancellationToken);
                    _playState = GetPlayState();
                    _sample = GetSample();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (samples <= 0) break;
                    samplesFilled += samples;
                }
                if (samplesFilled <= 0) break;
                if (preBufferLeft > 0)
                {
                    preBufferLeft -= samplesFilled / (double)_sampleRate;
                    if (preBufferLeft <= 0)
                    {
                        source = _source;
                        AL.SourcePlay(source);
                        Ce($"{nameof(AL)}.{nameof(AL.SourcePlay)} ({source})");
                    }
                }
                else
                {
                    if (PlayState != PlayState.Playing)
                    {
                        source = _source;
                        AL.SourceStop(source);
                        Ce($"{nameof(AL)}.{nameof(AL.SourceStop)} ({source})");
                        AL.SourcePlay(source);
                        Ce($"{nameof(AL)}.{nameof(AL.SourcePlay)} ({source})");
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
                                int processed;
                                source = _source;
                                ClearError();
                                AL.GetSource(source, ALGetSourcei.BuffersProcessed, out processed);
                                Ce($"{nameof(AL)}.{nameof(AL.GetSource)} ({source})", source);
                                if (await CleanupBuffersAsync(source, cancellationToken))
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
                    Ce($"{nameof(AL)}.{nameof(AL.GetSource)} ({source})");
                    AL.GetSource(source, ALGetSourcei.BuffersProcessed, out int processed);
                    Ce($"{nameof(AL)}.{nameof(AL.GetSource)} ({source})");
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
            Ce($"{nameof(AL)}.{nameof(AL.SourceStop)} ({source})");
            _playState = GetPlayState();
            _sample = GetSample();
        }
        catch (OperationCanceledException)
        {
            _running = false;
            source = _source;
            if (AL.IsSource(source))
            {
                AL.GetSource(source, ALGetSourcei.SampleOffset, out int sample);
                Ce($"{nameof(AL)}.{nameof(AL.GetSource)} ({source})");
                _sampleInBuffer = sample;
                AL.SourceStop(source);
                Ce($"{nameof(AL)}.{nameof(AL.SourceStop)}");
                _playState = GetPlayState();
                _sample = GetSample();
            }
            throw;
        }
        finally
        {
            source = _source;
            _source = 0;
            AL.DeleteSource(source);
            Ce($"{nameof(AL)}.{nameof(AL.DeleteSource)} ({source})");
            foreach (var bb in _sampleSizes.Keys)
            {
                AL.DeleteBuffer(bb);
                Ce($"{nameof(AL)}.{nameof(AL.DeleteBuffer)}");
            }
            _sampleSizes.Clear();
        }
    }

    private async Task<bool> CleanupBuffersAsync(int source, CancellationToken cancellationToken)
    {
        ClearError();
        AL.GetSource(source, ALGetSourcei.BuffersProcessed, out int processed);
        Ce($"{nameof(AL)}.{nameof(AL.GetSource)} ({source})");
        if (processed <= 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromMilliseconds(10));
            cancellationToken.ThrowIfCancellationRequested();
        }
        else
        {
            while (processed-- > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                source = _source;
                int bb = AL.SourceUnqueueBuffer(source);
                Ce($"{nameof(AL)}.{nameof(AL.SourceUnqueueBuffer)} ({source})");
                _processedSamples += _sampleSizes[bb];
                AL.DeleteBuffer(bb);
                Ce($"{nameof(AL)}.{nameof(AL.DeleteBuffer)}");
                _sampleSizes.Remove(bb);
                _playState = GetPlayState();
                _sample = GetSample();
                cancellationToken.ThrowIfCancellationRequested();
                return true;
            }
        }
        return false;
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

    private static void Ce(string op, int? source = null)
    {
        ALError error = AL.GetError();
        if (error == ALError.NoError) return;
        string e = AL.GetErrorString(error);
        string err = $"{op}::{error}: {e} {(source is { } sourceV ? AL.IsSource(sourceV) : "xx")}";
        throw new InvalidOperationException(err);
    }

    private void DestroyCurrentTask()
    {
        if (_activeSession == null) return;
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
