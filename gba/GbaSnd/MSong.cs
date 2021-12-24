namespace GbaSnd;

public abstract class MSong
{
    public virtual string Name => "Unnamed Song";
    public virtual string Album => "Unknown Album";
    public virtual string Artist => "Unknown Artist";
    public virtual TimeSpan? Duration => null;

    public abstract SoundGenerator GetGenerator();
}
