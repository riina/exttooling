using System.Collections;
using System.Diagnostics;
using OpenTK.Audio.OpenAL;

namespace Playful;

public sealed class MPlayer : IDisposable, IList<MSong>
{
    public bool Active => !_disposed;

    public bool Ended => _plEnded;

    private readonly TaggedPlaylist _songs;
    private readonly MPlayerContext _mPlayerContext;
    private readonly AutoResetEvent _are;
    private readonly ManualResetEvent _mre;
    private volatile int _vec;
    private Stopwatch _sw;
    private int _index;
    private bool _disposed;
    private volatile int _started;

    private MPlayerOutput? _output;
    private MSong? _song;
    private Guid _guid;
    private bool _plEnded;

    public MPlayer()
    {
        _mPlayerContext = new MPlayerContext();
        _songs = new TaggedPlaylist();
        _are = new AutoResetEvent(true);
        _mre = new ManualResetEvent(false);
        _sw = new Stopwatch();
    }

    public bool TryGetDisplayState(out MPlayerDisplayState displayState)
    {
        _are.WaitOne();
        try
        {
            if (_output != null && _song != null)
            {
                displayState = new MPlayerDisplayState(_songs.IndexOfGuid(_guid), _songs.Count, _output.TimeApprox, _output.Duration, _output.PlayState, _song.Name, _song.Album, _song.Artist, "");
                return true;
            }
        }
        catch
        {
            // ignored
        }
        finally
        {
            _are.Set();
        }
        displayState = default;
        return false;
    }

    public Task StartExecuteAsync(CancellationToken cancellationToken = default)
    {
        EnableStartOnce();
        EnsureNotDisposed();
        Task ex = ExecuteAsync(cancellationToken);
        _mre.WaitOne();
        return ex;
    }

    private async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _sw.Start();
            _are.WaitOne();
            _index = 0;
            _are.Set();
            while (true)
            {
                _are.WaitOne();
                try
                {
                    if (_index >= _songs.Count) break;
                    _index = Math.Max(_index, 0);
                    _song = _songs[_index];
                    _guid = _songs.Guids[_index];
                }
                finally
                {
                    _are.Set();
                }
                using MPlayerOutput p = _mPlayerContext.Stream(_song.GetGenerator());
                _are.WaitOne();
                try
                {
                    await p.PlayAsync(0, cancellationToken);
                    _output = p;
                }
                finally
                {
                    _are.Set();
                    _mre.Set();
                }
                int vec;
                while (true)
                {
                    _are.WaitOne();
                    try
                    {
                        if ((vec = Interlocked.Exchange(ref _vec, 0)) != 0)
                        {
                            if (vec == -1)
                            {
                                if (p.TimeApprox < 2.0)
                                    vec--;
                            }
                            else
                                vec--;
                            break;
                        }
                        if (p.PlayState == PlayState.Ended) break;
                    }
                    finally
                    {
                        _are.Set();
                    }
                    await Task.Delay(10, cancellationToken);
                }
                _are.WaitOne();
                try
                {
                    _index = _songs.IndexOfGuid(_guid) + 1 + vec;
                    _output = null;
                    _song = null;
                    _guid = default;
                }
                finally
                {
                    _are.Set();
                }
            }
            _plEnded = true;
        }
        finally
        {
            _mre.Set();
        }
    }

    public void Stop()
    {
        EnsureNotDisposed();
        _are.WaitOne();
        try
        {
            if (_output == null) return;
            _output.Stop();
        }
        finally
        {
            _are.Set();
        }
    }

    public async Task PlaySeekAsync(double delta = 0, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        _are.WaitOne();
        try
        {
            if (_output == null) return;
            await _output.PlaySeekAsync(delta, cancellationToken);
        }
        finally
        {
            _are.Set();
        }
    }

    public void SeekTrack(int delta)
    {
        if (delta == 0) return;
        _vec = delta;
    }

    private void EnableStartOnce()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 1) throw new InvalidOperationException("Cannot start display more than once");
    }

    private void EnsureNotDisposed()
    {
        if (_disposed) throw new InvalidOperationException();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _mPlayerContext.Dispose();
        _are.WaitOne();
        _are.Dispose();
    }

    public IEnumerator<MSong> GetEnumerator() => _songs.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_songs).GetEnumerator();

    public void Add(MSong item) => _songs.Add(item);

    public void Clear() => _songs.Clear();

    public bool Contains(MSong item) => _songs.Contains(item);

    public void CopyTo(MSong[] array, int arrayIndex) => _songs.CopyTo(array, arrayIndex);

    public bool Remove(MSong item) => _songs.Remove(item);

    public int Count => _songs.Count;

    public bool IsReadOnly => _songs.IsReadOnly;

    public int IndexOf(MSong item) => _songs.IndexOf(item);

    public void Insert(int index, MSong item) => _songs.Insert(index, item);

    public void RemoveAt(int index) => _songs.RemoveAt(index);

    public MSong this[int index]
    {
        get => _songs[index];
        set => _songs[index] = value;
    }
}
