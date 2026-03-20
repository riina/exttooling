namespace Playful;

public abstract class PlayableSong
{
    public string Name { get; }

    public string Album { get; }

    public string Artist { get; }

    public TimeSpan? Duration { get; }

    protected PlayableSong(string? name, string? album, string? artist, TimeSpan? duration)
    {
        Name = name ?? "Unnamed Song";
        Album = album ?? "Unknown Album";
        Artist = artist ?? "Unknown Artist";
        Duration = duration;
    }

    public abstract SoundGenerator GetGenerator();
}
