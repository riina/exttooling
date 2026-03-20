namespace Playful.Gba;

public class GbaSong : PlayableSong
{
    private readonly GbaSongSource _source;

    public int SongId { get; }

    internal GbaSong(GbaSongSource source, int songId, string album, string artist, string name, TimeSpan? duration = null)
        : base(name, album, artist, duration)
    {
        _source = source;
        SongId = songId;
    }

    internal GbaSong(GbaSongSource source, int songId, string gameCode, int index, string? makerName, TimeSpan? duration = null)
        : this(
            source: source,
            songId: songId,
            album: gameCode.Replace('_', ' ').Trim(),
            artist: makerName ?? "Unknown Artist",
            name: $"Track {index} (#{songId})",
            duration: duration
        )
    {
    }

    public override SoundGenerator GetGenerator() => _source.GetGenerator(SongId);
}
