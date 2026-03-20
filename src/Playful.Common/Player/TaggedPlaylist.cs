using System.Collections;

namespace Playful.Common.Player;

internal class TaggedPlaylist : IList<PlayableSong>
{
    internal readonly List<Guid> Guids;
    private readonly List<PlayableSong> _songs;

    public TaggedPlaylist()
    {
        _songs = new List<PlayableSong>();
        Guids = new List<Guid>();
    }

    public IEnumerator<PlayableSong> GetEnumerator() => _songs.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _songs.GetEnumerator();

    public void Add(PlayableSong item)
    {
        _songs.Add(item);
        Guids.Add(Guid.NewGuid());
    }

    public void Clear()
    {
        _songs.Clear();
        Guids.Clear();
    }

    public bool Contains(PlayableSong item) => _songs.Contains(item);

    public void CopyTo(PlayableSong[] array, int arrayIndex) => _songs.CopyTo(array, arrayIndex);

    public bool Remove(PlayableSong item)
    {
        int index = IndexOf(item);
        if (index == -1)
        {
            return false;
        }
        _songs.RemoveAt(index);
        Guids.RemoveAt(index);
        return true;
    }

    public int Count => _songs.Count;
    public bool IsReadOnly => false;

    public int IndexOf(PlayableSong item) => _songs.IndexOf(item);

    public int IndexOfGuid(Guid item) => Guids.IndexOf(item);

    public void Insert(int index, PlayableSong item)
    {
        _songs.Insert(index, item);
        Guids.Insert(index, Guid.NewGuid());
    }

    public void RemoveAt(int index)
    {
        _songs.RemoveAt(index);
        Guids.RemoveAt(index);
    }

    public PlayableSong this[int index]
    {
        get => _songs[index];
        set
        {
            _songs[index] = value;
            Guids[index] = Guid.NewGuid();
        }
    }
}
