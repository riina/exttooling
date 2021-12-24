namespace Playful.Gba;

public class GbaSong : MSong
{
    private readonly GbaSongSource _source;

    public int SongId { get; }
    public override string Name { get; }
    public override string Album { get; }
    public override string Artist { get; }
    public override TimeSpan? Duration { get; }

    internal GbaSong(GbaSongSource source, int songId, string album, string artist, string name, double? duration = null)
    {
        Name = name;
        Album = album;
        Artist = artist;
        _source = source;
        SongId = songId;
        Duration = duration is { } d ? TimeSpan.FromSeconds(d) : null;
    }

    internal GbaSong(GbaSongSource source, int songId, string gameCode, int index, string? makerName, double? duration = null)
    {
        Name = $"Track {index} (#{songId})";
        Album = gameCode.Replace('_', ' ').Trim();
        Artist = makerName ?? "Unknown Artist";
        _source = source;
        SongId = songId;
        Duration = duration is { } d ? TimeSpan.FromSeconds(d) : null;
    }

    public override SoundGenerator GetGenerator() => _source.GetGenerator(SongId);
}
