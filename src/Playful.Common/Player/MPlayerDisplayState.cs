namespace Playful.Common.Player;

public readonly struct MPlayerDisplayState
{
    public int Index { get; init; }
    public int Count { get; init; }
    public double Time { get; init; }
    public double TimeCacheStart { get; init; }
    public double TimeCacheEnd { get; init; }
    public double Duration { get; init; }
    public PlayState PlayState { get; init; }
    public string Name { get; init; }
    public string Album { get; init; }
    public string Artist { get; init; }
    public string? Debug { get; init; }
    public string? Message { get; init; }

    public MPlayerDisplayState(
        int index,
        int count,
        double time,
        double timeCacheStart,
        double timeCacheEnd,
        double duration,
        PlayState playState,
        string name,
        string album,
        string artist,
        string? debug,
        string? message)
    {
        Index = index;
        Count = count;
        Time = time;
        TimeCacheStart = timeCacheStart;
        TimeCacheEnd = timeCacheEnd;
        Duration = duration;
        PlayState = playState;
        Name = name;
        Album = album;
        Artist = artist;
        Debug = debug;
        Message = message;
    }
}
