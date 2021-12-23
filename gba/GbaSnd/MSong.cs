namespace GbaSnd;

public abstract class MSong
{
    public string Name { get; }

    protected MSong(string name) => Name = name;

    public abstract Stereo16Generator GetGenerator();
}
