using System.Collections;

namespace GbaSnd;

internal class TaggedPlaylist : IList<MSong>
{
    internal readonly List<Guid> Guids;
    private readonly List<MSong> _songs;

    public TaggedPlaylist()
    {
        _songs = new List<MSong>();
        Guids = new List<Guid>();
    }

    public IEnumerator<MSong> GetEnumerator() => _songs.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _songs.GetEnumerator();

    public void Add(MSong item)
    {
        _songs.Add(item);
        Guids.Add(Guid.NewGuid());
    }

    public void Clear()
    {
        _songs.Clear();
        Guids.Clear();
    }

    public bool Contains(MSong item) => _songs.Contains(item);

    public void CopyTo(MSong[] array, int arrayIndex) => _songs.CopyTo(array, arrayIndex);

    public bool Remove(MSong item)
    {
        int index = IndexOf(item);
        if (index == -1) return false;
        _songs.RemoveAt(index);
        Guids.RemoveAt(index);
        return true;
    }

    public int Count => _songs.Count;
    public bool IsReadOnly => false;

    public int IndexOf(MSong item) => _songs.IndexOf(item);

    public int IndexOfGuid(Guid item) => Guids.IndexOf(item);

    public void Insert(int index, MSong item)
    {
        _songs.Insert(index, item);
        Guids.Insert(index, Guid.NewGuid());
    }

    public void RemoveAt(int index)
    {
        _songs.RemoveAt(index);
        Guids.RemoveAt(index);
    }

    public MSong this[int index]
    {
        get => _songs[index];
        set
        {
            _songs[index] = value;
            Guids[index] = Guid.NewGuid();
        }
    }
}
