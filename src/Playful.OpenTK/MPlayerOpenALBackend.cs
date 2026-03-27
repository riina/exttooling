using System.Collections.Concurrent;
using OpenTK.Audio.OpenAL;

namespace Playful.OpenTK;

public sealed class MPlayerOpenALBackend : IPlayerBackend
{
    private readonly AutoResetEvent _areSource = new(true);
    private readonly AutoResetEvent _areBuffer = new(true);
    private readonly ConcurrentDictionary<int, int> _bufferToSampleCount = new();
    private readonly int _source;
    private readonly ALFormat _format;
    private readonly int _sampleRate;
    private readonly int _numChannels;
    private int _processedSamples;
    private int _sampleInBuffer;
    private bool _sameDesu;
    private bool _disposed;

    public static MPlayerOpenALBackend Create(AudioFormat format, int sampleRate)
    {
        return new MPlayerOpenALBackend(format, sampleRate);
    }

    public MPlayerOpenALBackend(AudioFormat format, int sampleRate)
    {
        _source = AL.GenSource();
        Ce(static _ => $"{nameof(AL)}.{nameof(AL.GenSource)}");
        _sampleRate = sampleRate;
        (_numChannels, _format) = format switch
        {
            AudioFormat.Pcm8X1 => (1, ALFormat.Mono8),
            AudioFormat.Pcm8X2 => (2, ALFormat.Stereo8),
            AudioFormat.Pcm16X1 => (1, ALFormat.Mono16),
            AudioFormat.Pcm16X2 => (2, ALFormat.Stereo16),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public int GetQueuedSamples() => _bufferToSampleCount.Values.Sum();

    public void Play(bool restart)
    {
        EnsureNotDisposed();
        if (restart)
        {
            AL.SourceStop(_source);
            Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.SourceStop)} ({ceSource})", _source);
        }
        AL.SourcePlay(_source);
        Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.SourcePlay)} ({ceSource})", _source);
    }

    public void Stop()
    {
        EnsureNotDisposed();
        AL.GetSource(_source, ALGetSourcei.SampleOffset, out int sample);
        Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.GetSource)} ({ceSource})", _source);
        _sampleInBuffer = sample;
        AL.SourceStop(_source);
        Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.SourceStop)} ({ceSource})", _source);
    }

    public async Task WaitForNextLoopAsync(Action iterationAction, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
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
                    ClearError();
                    AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out int _);
                    Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.GetSource)} ({ceSource})", _source);
                    int iterationProcessedSamples = await CleanupBuffersAsync(_source, iterationAction, cancellationToken);
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

    public async Task WaitForFinishAsync(Action iterationAction, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        // Wait for all buffers to finish processing
        _areBuffer.WaitOne();
        try
        {
            while (GetPlayState() == PlayState.Playing)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AL.GetSource(_source, ALGetSourcei.BuffersQueued, out int queued);
                Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.GetSource)} ({ceSource})", _source);
                AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out int processed);
                Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.GetSource)} ({ceSource})", _source);
                if (queued == 0 && processed == 0)
                {
                    break;
                }
                await CleanupBuffersAsync(_source, iterationAction, cancellationToken);
            }
        }
        finally
        {
            _areBuffer.Set();
        }
    }

    public int GetSampleOffset()
    {
        EnsureNotDisposed();
        _areSource.WaitOne();
        try
        {
            if (GetPlayState() != PlayState.Playing)
            {
                return _processedSamples + _sampleInBuffer;
            }
            AL.GetSource(_source, ALGetSourcei.SampleOffset, out int sample);
            Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.GetSource)} ({ceSource})", _source);
            return _processedSamples + (_sampleInBuffer = sample);
        }
        finally
        {
            _areSource.Set();
        }
    }

    public void QueueBuffer<TSample>(ReadOnlyMemory<TSample> data) where TSample : unmanaged
    {
        EnsureNotDisposed();
        int buf = AL.GenBuffer();
        Ce(static _ => $"{nameof(AL)}.{nameof(AL.GenBuffer)}");
        AL.BufferData(buf, _format, data.Span, _sampleRate);
        Ce(static _ => $"{nameof(AL)}.{nameof(AL.BufferData)}");
        _areSource.WaitOne();
        try
        {
            AL.SourceQueueBuffer(_source, buf);
            Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.SourceQueueBuffer)} ({ceSource})");
            _sameDesu = true;
        }
        finally
        {
            _areSource.Set();
        }
        _bufferToSampleCount[buf] = data.Length / _numChannels;
    }

    private async Task<int> CleanupBuffersAsync(int source, Action iterationAction, CancellationToken cancellationToken)
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
                if (!_bufferToSampleCount.TryRemove(bb, out int bufferSampleCount))
                {
                    throw new InvalidOperationException();
                }
                _processedSamples += bufferSampleCount;
                iterationProcessedSamples += bufferSampleCount;
                AL.DeleteBuffer(bb);
                Ce(static _ => $"{nameof(AL)}.{nameof(AL.DeleteBuffer)}", source);
                iterationAction();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return iterationProcessedSamples;
        }
        return 0;
    }

    public PlayState GetPlayState()
    {
        EnsureNotDisposed();
        AL.GetSource(_source, ALGetSourcei.SourceState, out int sta);
        Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.GetSource)} ({ceSource})", _source);
        return (ALSourceState)sta switch
        {
            ALSourceState.Initial => PlayState.Initial,
            ALSourceState.Playing => PlayState.Playing,
            ALSourceState.Paused => PlayState.Paused,
            ALSourceState.Stopped => PlayState.Stopped,
            _ => PlayState.Unknown
        };
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

    private void ReleaseUnmanagedResources()
    {
        if (AL.IsSource(_source))
        {
            AL.DeleteSource(_source);
            Ce(static ceSource => $"{nameof(AL)}.{nameof(AL.DeleteSource)} ({ceSource})", _source);
        }
    }

    private void Dispose(bool disposing)
    {
        ReleaseUnmanagedResources();
        _disposed = true;
        if (disposing)
        {
            _areSource.Dispose();
            _areBuffer.Dispose();
            foreach (int bb in _bufferToSampleCount.Keys)
            {
                AL.DeleteBuffer(bb);
                Ce(static _ => $"{nameof(AL)}.{nameof(AL.DeleteBuffer)}");
            }
            _bufferToSampleCount.Clear();
        }
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~MPlayerOpenALBackend() => Dispose(false);
}
