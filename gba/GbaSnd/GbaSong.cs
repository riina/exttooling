namespace GbaSnd;

public class GbaSong : MSong
{
    private readonly GbaSongLoader _loader;
    private readonly int _songId;

    public override string Name { get; }
    public override string Album { get; }
    public override string Artist { get; }
    public override TimeSpan? Duration { get; }

    internal GbaSong(GbaSongLoader loader, int songId, string album, string artist, string name, double? duration = null)
    {
        Name = name;
        Album = album;
        Artist = artist;
        _loader = loader;
        _songId = songId;
        Duration = duration is { } d ? TimeSpan.FromSeconds(d) : null;
    }

    internal GbaSong(GbaSongLoader loader, int songId, string gameCode, int index, string? makerName, double? duration = null)
    {
        Name = $"Track {index} (#{songId})";
        Album = gameCode.Replace('_', ' ').Trim();
        Artist = makerName ?? "Unknown Artist";
        _loader = loader;
        _songId = songId;
        Duration = duration is { } d ? TimeSpan.FromSeconds(d) : null;
    }

    public override SoundGenerator GetGenerator() => _loader.GetGenerator(_songId);
}
