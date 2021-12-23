namespace GbaSnd;

public class GbaSong : MSong
{
    private readonly GbaSongLoader _loader;
    private readonly int _songId;

    internal GbaSong(string name, GbaSongLoader loader, int songId) : base(name) => (_loader, _songId) = (loader, songId);
    internal GbaSong(GbaSongLoader loader, int index, int songId) : base($"Track {index} (#{songId})") => (_loader, _songId) = (loader, songId);
    public override SoundGenerator GetGenerator() => _loader.GetGenerator(_songId);
}
