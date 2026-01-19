using System.Diagnostics.CodeAnalysis;
using GbaMus;

namespace Playful.Gba;

[SongLoaderInfo("gba")]
public class GbaSongLoader : SongLoader
{
    public override bool TryLoadSongs(Stream stream, Uri uri, [NotNullWhen(true)] out IReadOnlyCollection<MSong>? songs)
    {
        string localPath = uri.LocalPath;
        if (!".gba".Equals(Path.GetExtension(localPath), StringComparison.InvariantCultureIgnoreCase)) goto fail;
        string frag = uri.Fragment;
        List<int> songIds = new();
        if (!string.IsNullOrEmpty(frag))
            foreach (string songIdStr in frag[1..].Split(','))
                if (int.TryParse(songIdStr, out int songId))
                    songIds.Add(songId);
        try
        {
            GbaSongSource source = new(stream, new GbaMusRipper.Settings(ImproveSoundfontCompliance: true));
            songs = songIds.Any() ? source.Songs.Where(s => songIds.Contains(s.SongId)).OfType<MSong>().ToList() : new List<MSong>(source.Songs);
            return true;
        }
        catch
        {
            // ignored
        }
        fail:
        songs = null;
        return false;
    }
}
