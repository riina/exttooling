using System.Collections;
using System.Diagnostics;

namespace Playful.Common.Player;

public sealed class MPlayer : IDisposable, IList<PlayableSong>
{
    public bool Active => !_disposed;

    public bool Ended => _plEnded;

    private readonly TaggedPlaylist _songs;
    private readonly IPlayerContext _mPlayerContext;
    private readonly AutoResetEvent _are;
    private readonly ManualResetEvent _mre;
    private volatile int _vec;
    private Stopwatch _sw;
    private int _index;
    private bool _disposed;
    private volatile int _started;

    private MPlayerOutput? _output;
    private PlayableSong? _song;
    private Guid _guid;
    private bool _plEnded;

    public MPlayer(MPlayerContextCreationDelegate contextCreationDelegate)
    {
        _mPlayerContext = contextCreationDelegate();
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
                displayState = new MPlayerDisplayState(
                    _songs.IndexOfGuid(_guid),
                    _songs.Count,
                    _output.TimeApprox,
                    _output.TimeCacheStart,
                    _output.TimeCacheEnd,
                    _output.Duration,
                    _output.PlayState,
                    _song.Name,
                    _song.Album,
                    _song.Artist,
                    _output.Debug,
                    "");
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
                    if (_index >= _songs.Count)
                    {
                        break;
                    }
                    _index = Math.Max(_index, 0);
                    _song = _songs[_index];
                    _guid = _songs.Guids[_index];
                }
                finally
                {
                    _are.Set();
                }
                using MPlayerOutput p = new(_song.GetGenerator(), _mPlayerContext.CreateBackend);
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
                                {
                                    vec--;
                                }
                            }
                            else
                            {
                                vec--;
                            }
                            break;
                        }
                        if (p.PlayState == PlayState.Ended)
                        {
                            break;
                        }
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
                    _guid = Guid.Empty;
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
            _output?.Stop();
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
            if (_output == null)
            {
                return;
            }
            await _output.PlaySeekAsync(delta, cancellationToken);
        }
        finally
        {
            _are.Set();
        }
    }

    public async Task SeekMaintainStateAsync(double delta = 0, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        _are.WaitOne();
        try
        {
            if (_output == null)
            {
                return;
            }
            await _output.SeekMaintainStateAsync(delta, cancellationToken);
        }
        finally
        {
            _are.Set();
        }
    }

    public void SeekTrack(int delta)
    {
        if (delta == 0)
        {
            return;
        }
        _vec = delta;
    }

    private void EnableStartOnce()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 1)
        {
            throw new InvalidOperationException("Cannot start display more than once");
        }
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new InvalidOperationException();
        }
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

    public IEnumerator<PlayableSong> GetEnumerator() => _songs.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_songs).GetEnumerator();

    public void Add(PlayableSong item) => _songs.Add(item);

    public void Clear() => _songs.Clear();

    public bool Contains(PlayableSong item) => _songs.Contains(item);

    public void CopyTo(PlayableSong[] array, int arrayIndex) => _songs.CopyTo(array, arrayIndex);

    public bool Remove(PlayableSong item) => _songs.Remove(item);

    public int Count => _songs.Count;

    public bool IsReadOnly => _songs.IsReadOnly;

    public int IndexOf(PlayableSong item) => _songs.IndexOf(item);

    public void Insert(int index, PlayableSong item) => _songs.Insert(index, item);

    public void RemoveAt(int index) => _songs.RemoveAt(index);

    public PlayableSong this[int index]
    {
        get => _songs[index];
        set => _songs[index] = value;
    }
}
