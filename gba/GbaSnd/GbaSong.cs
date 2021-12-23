namespace GbaSnd;

public class GbaSong : MSong
{
    private readonly GbaSongLoader _loader;
    private readonly int _songId;

    public override string Name { get; }
    public override string Album { get; }
    public override string Artist { get; }

    internal GbaSong(GbaSongLoader loader, int songId, string album, string artist, string name)
    {
        Name = name;
        Album = album;
        Artist = artist;
        _loader = loader;
        _songId = songId;
    }

    internal GbaSong(GbaSongLoader loader, int songId, string gameCode, int index, string? makerName)
    {
        Name = $"Track {index} (#{songId})";
        Album = gameCode.Replace('_', ' ').Trim();
        Artist = makerName ?? "Unknown Artist";
        _loader = loader;
        _songId = songId;
    }

    public override SoundGenerator GetGenerator() => _loader.GetGenerator(_songId);
}
