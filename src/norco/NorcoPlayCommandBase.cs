using System.CommandLine;

namespace norco;

public sealed class NorcoPlayCommandBase
{
    private readonly Option<ushort?> _listenPortOption;
    private readonly Option<FileInfo> _playlistOption;

    public NorcoPlayCommandBase()
    {
        _listenPortOption = new Option<ushort?>("-l", "--listen") { HelpName = "listen port", Description = "Listen port" };
        _playlistOption = new Option<FileInfo>("-p", "--playlist") { HelpName = "playlist", Description = "Playlist to play" };
    }

    public void AddToCommand(Command command)
    {
        command.Add(_listenPortOption);
        command.Add(_playlistOption);
    }

    public async Task<int> RunAsync(ParseResult parseResult)
    {
        ushort? listenPort = parseResult.GetValue(_listenPortOption);
        FileInfo? playlist = parseResult.GetValue(_playlistOption);
        using NorcoManager nm = new(new NorcoOptions(listenPort));
        await nm.ExecuteAsync();
        return 0;
    }
}
