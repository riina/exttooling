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

    private readonly Stereo16StreamGenerator _stereo16StreamGenerator;
    private readonly int _sampleRate;
    private readonly TextWriter? _debug;
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
        _source = AL.GenSource();
        Ce();
        _debug = debug;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureState();
        DestroyCurrentTask();
        _activeSession = StartStreamData();
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

    private ActiveSession StartStreamData()
    {
        _stereo16StreamGenerator.Reset();
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
                short[] dataTmp = ArrayPool<short>.Shared.Rent(BufferSizeInSamples * 2);
                try
                {
                    if (_debug != null) await _debug.WriteAsync("Waiting for buffer... ");
                    int samples = await _stereo16StreamGenerator.FillBufferAsync(dataTmp, cancellationToken);
                    if (_debug != null) await _debug.WriteLineAsync($"{samples} samples");
                    if (samples <= 0) break;
                    Memory<short> data = dataTmp.AsMemory(0, samples * 2);
                    int buf = AL.GenBuffer();
                    Ce();
                    AL.BufferData(buf, ALFormat.Stereo16, data.Span, _sampleRate);
                    Ce();
                    AL.SourceQueueBuffer(_source, buf);
                    Ce();
                    if (preBufferLeft > 0)
                    {
                        preBufferLeft -= samples / (double)_sampleRate;
                        if (preBufferLeft <= 0)
                        {
                            AL.SourcePlay(_source);
                            Ce();
                        }
                    }
                    else
                    {
                        // Wait for at least one buffer to finish processing
                        while (true)
                        {
                            AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out int processed);
                            if (processed <= 0) await Task.Delay(10, cancellationToken);
                            else
                            {
                                while (processed-- > 0)
                                {
                                    int bb = AL.SourceUnqueueBuffer(_source);
                                    Ce();
                                    AL.DeleteBuffer(bb);
                                    Ce();
                                }
                                break;
                            }
                        }
                    }
                }
                finally
                {
                    ArrayPool<short>.Shared.Return(dataTmp);
                }
            }
            // Wait for all buffers to finish processing
            while (true)
            {
                AL.GetSource(_source, ALGetSourcei.BuffersQueued, out int queued);
                Ce();
                AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out int processed);
                if (_debug != null) await _debug.WriteLineAsync($"waiting... {queued} {processed}");
                Ce();
                if (queued == 0 && processed == 0) break;
                if (processed <= 0) await Task.Delay(10, cancellationToken);
                else
                {
                    while (processed-- > 0)
                    {
                        int bb = AL.SourceUnqueueBuffer(_source);
                        Ce();
                        AL.DeleteBuffer(bb);
                        Ce();
                    }
                }
            }
            AL.SourceStop(_source);
        }
        catch (TaskCanceledException)
        {
            AL.SourceStop(_source);
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
                _activeSession = null;
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
        GC.SuppressFinalize(this);
    }

    ~PCtx() => ReleaseUnmanagedResources();
}
