using System.Collections;

namespace GbaSnd;

public sealed class MPlayer : IDisposable, IList<MSong>
{
    private readonly TaggedPlaylist _songs;
    private readonly MPlayerContext _mPlayerContext;
    private readonly AutoResetEvent _are;
    private int _index;

    public MPlayer()
    {
        _mPlayerContext = new MPlayerContext();
        _songs = new TaggedPlaylist();
        _are = new AutoResetEvent(true);
    }

    public async Task ExecuteAsync()
    {
        _are.WaitOne();
        _index = 0;
        _are.Set();
        while (true)
        {
            MSong song;
            Guid guid;
            _are.WaitOne();
            try
            {
                if (_index >= _songs.Count) break;
                song = _songs[_index];
                guid = _songs.Guids[_index];
            }
            finally
            {
                _are.Set();
            }
            using MPlayerOutput p = _mPlayerContext.Stream(song.GetGenerator());
            await p.PlayAsync();
            Task prevTask = Task.CompletedTask;
            while (true)
            {
                await Task.Delay(10);
                await prevTask;
                if (p.PlayState == PlayState.Ended) break;
                int transport = 0;
                bool playing = p.PlayState == PlayState.Playing;
                bool setPlaying = playing;
                bool spaceLast = false;
                while (Console.KeyAvailable)
                {
                    ConsoleKeyInfo cki = Console.ReadKey(true);
                    switch (cki.Key)
                    {
                        case ConsoleKey.LeftArrow:
                            spaceLast = false;
                            transport -= 5;
                            break;
                        case ConsoleKey.RightArrow:
                            spaceLast = false;
                            transport += 5;
                            break;
                        case ConsoleKey.Spacebar:
                            spaceLast = true;
                            setPlaying ^= true;
                            break;
                    }
                }
                if (!setPlaying && playing && spaceLast) p.Stop();
                if (transport != 0 || setPlaying && !playing) prevTask = p.PlaySeekAsync(transport);
                else prevTask = Task.CompletedTask;
            }
            try
            {
                _index = _songs.IndexOfGuid(guid) + 1;
            }
            finally
            {
                _are.Set();
            }
        }
    }

    public void Dispose() => _mPlayerContext.Dispose();

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
