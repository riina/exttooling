using System.Buffers;
using OpenTK.Audio.OpenAL;

namespace GbaSnd;

public sealed class PCtx : IDisposable
{
    private const int BufferSizeInSamples = 8 * 1024;
    private const double PreBufferInSeconds = 0.5;

    public PlayState PlayState
    {
        get
        {
            EnsureState();
            if (GetPlayTaskResetIfComplete().IsCompleted) return PlayState.Stopped;
            var state = AL.GetSourceState(_source) switch
            {
                ALSourceState.Initial => PlayState.Initial,
                ALSourceState.Playing => PlayState.Playing,
                ALSourceState.Paused => PlayState.Paused,
                ALSourceState.Stopped => PlayState.Stopped,
                _ => throw new ArgumentOutOfRangeException()
            };
            Ce();
            return state;
        }
    }

    public double Time => GetTimeFromSample(Sample);

    public int Sample
    {
        get
        {
            EnsureState();
            WaitForBuffersAsync().Wait();
            if (GetPlayTaskResetIfComplete().IsCompleted) return _baseSample + _processedSamples + _sampleInBuffer;
            AL.GetSource(_source, ALGetSourcei.SampleOffset, out int sample);
            Ce();
            return _baseSample + _processedSamples + (_sampleInBuffer = sample);
        }
    }

    public double Duration => GetTimeFromSample(Length);

    public int Length { get; }

    private bool _sameDesu;
    private int _processedSamples;
    private int _sampleInBuffer;

    private readonly Stereo16StreamGenerator _stereo16StreamGenerator;
    private readonly int _sampleRate;
    private readonly TextWriter? _debug;
    private readonly Dictionary<int, int> _sampleSizes;
    private readonly AutoResetEvent _are;
    private int _baseSample;
    private int _source;
    private ActiveSession? _activeSession;

    private record ActiveSession(Task Task, CancellationTokenSource Cts)
    {
        public void Stop() => Cts.Cancel();
    }

    internal PCtx(Stereo16StreamGenerator stereo16StreamGenerator, TextWriter? debug = null)
    {
        _stereo16StreamGenerator = stereo16StreamGenerator;
        _sampleRate = _stereo16StreamGenerator.Frequency;
        Length = _stereo16StreamGenerator.Length;
        _source = AL.GenSource();
        Ce();
        _sampleSizes = new Dictionary<int, int>();
        _are = new AutoResetEvent(true);
        _debug = debug;
    }

    public async Task StartAsync(double time = 0, CancellationToken cancellationToken = default)
    {
        EnsureState();
        DestroyCurrentTask();
        _activeSession = StartStreamData(GetSampleFromTime(time));
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
                    await Task.Delay(10, cancellationToken);
                    break;
            }
        }
    }

    private int GetSampleFromTime(double time) => (int)(time * _sampleRate);

    private double GetTimeFromSample(int sample) => sample / (double)_sampleRate;

    private ActiveSession StartStreamData(int sample)
    {
        _baseSample = sample;
        _processedSamples = 0;
        _stereo16StreamGenerator.Reset(sample);
        CancellationTokenSource cts = new();
        Task streamData = StreamData(cts.Token);
        return new ActiveSession(streamData, cts);
    }

    public Task GetPlayTask()
    {
        EnsureState();
        return GetPlayTaskResetIfComplete();
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
                    int elementCount = (BufferSizeInSamples - samplesFilled) * 2;
                    short[] dataTmp = ArrayPool<short>.Shared.Rent(elementCount);
                    if (_debug != null) await _debug.WriteAsync("Waiting for buffer... ");
                    int samples;
                    try
                    {
                        samples = await _stereo16StreamGenerator.FillBufferAsync(dataTmp.AsMemory(0, elementCount), cancellationToken);
                    }
                    finally
                    {
                        ArrayPool<short>.Shared.Return(dataTmp);
                    }
                    if (_debug != null) await _debug.WriteLineAsync($"{samples} samples");
                    if (samples <= 0) break;
                    Memory<short> data = dataTmp.AsMemory(0, samples * 2);
                    int buf = AL.GenBuffer();
                    Ce();
                    AL.BufferData(buf, ALFormat.Stereo16, data.Span, _sampleRate);
                    Ce();
                    _are.WaitOne();
                    try
                    {
                        AL.SourceQueueBuffer(_source, buf);
                        Ce();
                        _sameDesu = true;
                    }
                    finally
                    {
                        _are.Set();
                    }
                    samplesFilled += samples;
                    _sampleSizes[buf] = samples;
                }
                if (samplesFilled <= 0) break;
                if (preBufferLeft > 0)
                {
                    preBufferLeft -= samplesFilled / (double)_sampleRate;
                    if (preBufferLeft <= 0)
                    {
                        AL.SourcePlay(_source);
                        Ce();
                    }
                }
                else
                {
                    // Wait for at least one buffer to finish processing
                    await WaitForBuffersAsync(cancellationToken);
                }
            }
            // Wait for all buffers to finish processing
            _are.WaitOne();
            try
            {
                while (true)
                {
                    AL.GetSource(_source, ALGetSourcei.BuffersQueued, out int queued);
                    Ce();
                    AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out int processed);
                    Ce();
                    if (queued == 0 && processed == 0) break;
                    if (processed <= 0) await Task.Delay(10, cancellationToken);
                    else
                    {
                        while (processed-- > 0)
                        {
                            int bb = AL.SourceUnqueueBuffer(_source);
                            Ce();
                            _processedSamples += _sampleSizes[bb];
                            AL.DeleteBuffer(bb);
                            Ce();
                        }
                    }
                }
            }
            finally
            {
                _are.Set();
            }
            _sampleInBuffer = 0;
            AL.SourceStop(_source);
        }
        catch (TaskCanceledException)
        {
            AL.GetSource(_source, ALGetSourcei.SampleOffset, out int sample);
            Ce();
            _sampleInBuffer = sample;
            AL.SourceStop(_source);
            throw;
        }
    }

    private async Task WaitForBuffersAsync(CancellationToken cancellationToken = default)
    {
        _are.WaitOne();
        try
        {
            if (_sameDesu) _sameDesu = false;
            else return;
            while (true)
            {
                AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out int processed);
                Ce();
                if (processed <= 0) await Task.Delay(10, cancellationToken);
                else
                {
                    while (processed-- > 0)
                    {
                        int bb = AL.SourceUnqueueBuffer(_source);
                        Ce();
                        _processedSamples += _sampleSizes[bb];
                        AL.DeleteBuffer(bb);
                        Ce();
                    }
                    break;
                }
            }
        }
        finally
        {
            _are.Set();
        }
    }

    private void EnsureState()
    {
        if (_source == 0) throw new ObjectDisposedException(nameof(PCtx));
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
        if (_source != 0) AL.DeleteSource(_source);
        _source = 0;
        Ce();
        _source = AL.GenSource();
        Ce();
    }

    private void Ce()
    {
        ALError error = AL.GetError();
        if (error == ALError.NoError) return;
        string e = AL.GetErrorString(error);
        DestroyCurrentTask();
        throw new InvalidOperationException(e);
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
        SwapSource();
    }

    private void ReleaseUnmanagedResources()
    {
        DestroyCurrentTask();
        if (_source != 0) AL.DeleteSource(_source);
        _source = 0;
        Ce();
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        _are.Dispose();
        GC.SuppressFinalize(this);
    }

    ~PCtx() => ReleaseUnmanagedResources();
}
