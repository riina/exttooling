using System.CommandLine;

namespace norco;

public sealed class NorcoPlayCommandBase
{
    private readonly Option<ushort?> _listenPortOption;
    private readonly Option<FileInfo> _playlistOption;
    private readonly Option<bool> _showDebugOption;
    private readonly Option<bool> _showCacheOption;

    public NorcoPlayCommandBase()
    {
        _listenPortOption = new Option<ushort?>("-l", "--listen") { HelpName = "listen port", Description = "Listen port" };
        _playlistOption = new Option<FileInfo>("-p", "--playlist") { HelpName = "playlist", Description = "Playlist to play" };
        _showDebugOption = new Option<bool>("--show-debug") { Description = "Show debug output" };
        _showCacheOption = new Option<bool>("--show-cache") { Description = "Show cache info" };
    }

    public void AddToCommand(Command command)
    {
        command.Add(_listenPortOption);
        command.Add(_playlistOption);
        command.Add(_showDebugOption);
        command.Add(_showCacheOption);
    }

    public async Task<int> RunAsync(ParseResult parseResult)
    {
        ushort? listenPort = parseResult.GetValue(_listenPortOption);
        FileInfo? playlist = parseResult.GetValue(_playlistOption);
        bool showDebug = parseResult.GetValue(_showDebugOption);
        bool showCacheInfo = parseResult.GetValue(_showCacheOption);
        using NorcoManager nm = new(new NorcoOptions(listenPort, showDebug, showCacheInfo));
        await nm.ExecuteAsync();
        return 0;
    }
}
