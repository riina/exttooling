using CommandLine;

namespace norco;

public class NorcoOptions
{
    [Option('l', "listen")] public ushort? ListenPort { get; init; }

    [Option('p', "playlist")] public string? Playlist { get; init; }
}
