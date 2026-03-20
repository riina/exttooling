using Azura;

namespace Playful;

[Azura]
public readonly partial struct MPlayerDisplayState
{
    [Azura] public readonly int Index { get; init; }
    [Azura] public readonly int Count { get; init; }
    [Azura] public readonly double Time { get; init; }
    [Azura] public readonly double Duration { get; init; }
    [Azura] public readonly PlayState PlayState { get; init; }
    [Azura] public readonly string Name { get; init; }
    [Azura] public readonly string Album { get; init; }
    [Azura] public readonly string Artist { get; init; }
    [Azura] public  string? Message { get; init; }

    public MPlayerDisplayState(int index, int count, double time, double duration, PlayState playState, string name, string album, string artist, string? message)
    {
        Index = index;
        Count = count;
        Time = time;
        Duration = duration;
        PlayState = playState;
        Name = name;
        Album = album;
        Artist = artist;
        Message = message;
    }
}
