using System.Diagnostics.CodeAnalysis;

namespace Playful;

public abstract class SongLoader
{
    public abstract bool TryLoadSongs(Stream stream, Uri uri, [NotNullWhen(true)] out IReadOnlyCollection<PlayableSong>? songs);
}
