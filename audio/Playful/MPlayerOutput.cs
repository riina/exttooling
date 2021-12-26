using System.Buffers;
using OpenTK.Audio.OpenAL;

namespace Playful;

public sealed class MPlayerOutput : IDisposable
{
    private const int BufferSizeInSamples = 8 * 1024;
    private const double PreBufferInSeconds = 0.5;

    public PlayState PlayState
    {
        get
        {
            EnsureState();
            if (_ended) return PlayState.Ended;
            if (GetPlayTaskResetIfComplete().IsCompleted) return PlayState.Stopped;
            var sta = AL.GetSourceState(_source);
            try
            {
                Ce($"{nameof(AL)}.{nameof(AL.GetSourceState)} ({_source})");
            }
            catch (InvalidOperationException)
            {
                if (AL.GetErrorString(AL.GetError()) == "Invalid Enum") return PlayState.Unknown;
                throw;
            }
            var state = sta switch
            {
                ALSourceState.Initial => PlayState.Initial,
                ALSourceState.Playing => PlayState.Playing,
                ALSourceState.Paused => PlayState.Paused,
                ALSourceState.Stopped => PlayState.Stopped,
                _ => PlayState.Unknown
            };
            return state;
        }
    }

    public double TimeApprox => GetTimeFromSample(Sample);

    public int Sample
    {
        get
        {
            EnsureState();
            if (GetPlayTaskResetIfComplete().IsCompleted) return _baseSample + _processedSamples + _sampleInBuffer;
            _areSource.WaitOne();
            try
            {
                AL.GetSource(_source, ALGetSourcei.SampleOffset, out int sample);
                Ce($"{nameof(AL)}.{nameof(AL.GetSource)} ({_source})");
                return _baseSample + _processedSamples + (_sampleInBuffer = sample);
            }
            finally
            {
                _areSource.Set();
            }
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
    private volatile int _source;
    private ActiveSession? _activeSession;
    private bool _ended;

    private record ActiveSession(Task Task, CancellationTokenSource Cts)
    {
        public void Stop() => Cts.Cancel();
    }

    internal MPlayerOutput(SoundGenerator generator, TextWriter? debug = null)
    {
        _generator = generator;
        _sampleRate = _generator.Frequency;
        Length = _generator.Length;
        _source = AL.GenSource();
        Ce($"{nameof(AL)}.{nameof(AL.GenSource)}");
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
        sample = ClampSample(sample);
        EnsureState();
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
        EnsureState();
        DestroyCurrentTask();
    }

    private int ClampSample(int sample) => Math.Clamp(sample, 0, Length);

    private int GetSampleFromTime(double time) => (int)(time * _sampleRate);

    private double GetTimeFromSample(int sample) => sample / (double)_sampleRate;

    private ActiveSession StartStreamData(int sample)
    {
        _baseSample = sample;
        _processedSamples = 0;
        _generator.Reset(sample);
        CancellationTokenSource cts = new();
        Task streamData = StreamData(cts.Token);
        return new ActiveSession(streamData, cts);
    }

    public Task GetPlayTask()
    {
        EnsureState();
        return GetPlayTaskResetIfComplete();
    }

    private async Task<int> QueueAsync(int wantedSamples, CancellationToken cancellationToken = default)
    {
        return _generator switch
        {
            SoundGenerator<byte> s => await QueueAsync(s, wantedSamples, cancellationToken),
            SoundGenerator<sbyte> s => await QueueAsync(s, wantedSamples, cancellationToken),
            SoundGenerator<short> s => await QueueAsync(s, wantedSamples, cancellationToken),
            SoundGenerator<ushort> s => await QueueAsync(s, wantedSamples, cancellationToken),
            _ => throw new ArgumentException()
        };
    }

    private async Task<int> QueueAsync<TSample>(SoundGenerator<TSample> generator, int wantedSamples, CancellationToken cancellationToken = default) where TSample : unmanaged
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
            if (_debug != null) await _debug.WriteAsync("Waiting for buffer... ");
            samples = await generator.FillBufferAsync(wantedSamples, dataTmp.AsMemory(0, elementCount), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            ArrayPool<TSample>.Shared.Return(dataTmp);
        }
        if (_debug != null) await _debug.WriteLineAsync($"{samples} samples");
        if (samples <= 0) return 0;
        Memory<TSample> data = dataTmp.AsMemory(0, samples * numChannels);
        int buf = AL.GenBuffer();
        Ce($"{nameof(AL)}.{nameof(AL.GenBuffer)}");
        AL.BufferData(buf, format, data.Span, _sampleRate);
        Ce($"{nameof(AL)}.{nameof(AL.BufferData)}");
        cancellationToken.ThrowIfCancellationRequested();
        _areSource.WaitOne();
        try
        {
            AL.SourceQueueBuffer(_source, buf);
            Ce($"{nameof(AL)}.{nameof(AL.SourceQueueBuffer)}");
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
        EnsureState();
        double preBufferLeft = PreBufferInSeconds;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int samplesFilled = 0;
                while (samplesFilled < BufferSizeInSamples)
                {
                    int samples = await QueueAsync(BufferSizeInSamples - samplesFilled, cancellationToken);
                    if (samples <= 0) break;
                    samplesFilled += samples;
                }
                if (samplesFilled <= 0) break;
                if (preBufferLeft > 0)
                {
                    preBufferLeft -= samplesFilled / (double)_sampleRate;
                    if (preBufferLeft <= 0)
                    {
                        AL.SourcePlay(_source);
                        Ce($"{nameof(AL)}.{nameof(AL.SourcePlay)}");
                    }
                }
                else
                {
                    if (PlayState != PlayState.Playing)
                    {
                        AL.SourceStop(_source);
                        Ce($"{nameof(AL)}.{nameof(AL.SourceStop)}");
                        AL.SourcePlay(_source);
                        Ce($"{nameof(AL)}.{nameof(AL.SourcePlay)}");
                    }
                    // Wait for at least one buffer to finish processing
                    await WaitForBuffersAsync(cancellationToken);
                }
            }
            // Wait for all buffers to finish processing
            _areBuffer.WaitOne();
            try
            {
                while (PlayState == PlayState.Playing)
                {
                    AL.GetSource(_source, ALGetSourcei.BuffersQueued, out int queued);
                    Ce($"{nameof(AL)}.{nameof(AL.GetSource)} ({_source})");
                    AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out int processed);
                    Ce($"{nameof(AL)}.{nameof(AL.GetSource)} ({_source})");
                    if (queued == 0 && processed == 0) break;
                    if (processed <= 0) await Task.Delay(10, cancellationToken);
                    else
                    {
                        while (processed-- > 0)
                        {
                            int bb = AL.SourceUnqueueBuffer(_source);
                            Ce($"{nameof(AL)}.{nameof(AL.SourceUnqueueBuffer)}");
                            _processedSamples += _sampleSizes[bb];
                            AL.DeleteBuffer(bb);
                            Ce($"{nameof(AL)}.{nameof(AL.DeleteBuffer)}");
                        }
                    }
                }
            }
            finally
            {
                _areBuffer.Set();
            }
            _sampleInBuffer = 0;
            _ended = true;
            AL.SourceStop(_source);
            Ce($"{nameof(AL)}.{nameof(AL.SourceStop)}");
        }
        catch (TaskCanceledException)
        {
            if (AL.IsSource(_source))
            {
                AL.GetSource(_source, ALGetSourcei.SampleOffset, out int sample);
                Ce($"{nameof(AL)}.{nameof(AL.GetSource)} ({_source})");
                _sampleInBuffer = sample;
                AL.SourceStop(_source);
                Ce($"{nameof(AL)}.{nameof(AL.SourceStop)}");
            }
            throw;
        }
    }

    private async Task WaitForBuffersAsync(CancellationToken cancellationToken = default)
    {
        _areBuffer.WaitOne();
        try
        {
            if (_sameDesu) _sameDesu = false;
            else return;
            int runs = 100;
            while (runs-- > 0)
            {
                AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out int processed);
                Ce($"{nameof(AL)}.{nameof(AL.GetSource)} ({_source})");
                if (processed <= 0) await Task.Delay(10, cancellationToken);
                else
                {
                    while (processed-- > 0)
                    {
                        int bb = AL.SourceUnqueueBuffer(_source);
                        Ce($"{nameof(AL)}.{nameof(AL.SourceUnqueueBuffer)}");
                        _processedSamples += _sampleSizes[bb];
                        AL.DeleteBuffer(bb);
                        Ce($"{nameof(AL)}.{nameof(AL.DeleteBuffer)}");
                    }
                    break;
                }
            }
        }
        finally
        {
            _areBuffer.Set();
        }
    }

    private void EnsureState()
    {
        if (_source == 0) throw new ObjectDisposedException(nameof(MPlayerOutput));
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

    private void SwapSource()
    {
        int source;
        _areSource.WaitOne();
        try
        {
            if ((source = Interlocked.Exchange(ref _source, 0)) != 0)
            {
                AL.DeleteSource(source);
                Ce($"{nameof(AL)}.{nameof(AL.DeleteSource)} ({source})");
            }
            _source = AL.GenSource();
            Ce($"{nameof(AL)}.{nameof(AL.GenSource)}");
        }
        finally
        {
            _areSource.Set();
        }
    }

    private void Ce(string op)
    {
        ALError error = AL.GetError();
        if (error == ALError.NoError) return;
        string e = AL.GetErrorString(error);
        string err = $"{op}: {e}";
        throw new InvalidOperationException(err);
    }

    private void DestroyCurrentTask(bool destroyDestroy = false)
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
        if (!destroyDestroy) SwapSource();
    }

    private void ReleaseUnmanagedResources()
    {
        DestroyCurrentTask(true);
        int source;
        if ((source = Interlocked.Exchange(ref _source, 0)) != 0)
        {
            AL.DeleteSource(source);
            Ce($"{nameof(AL)}.{nameof(AL.DeleteSource)} ({source})");
        }
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        _areSource.Dispose();
        _areBuffer.Dispose();
        GC.SuppressFinalize(this);
    }

    ~MPlayerOutput() => ReleaseUnmanagedResources();
}
