using System.CommandLine;
using Playful;

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
        MPlayerContextCreationDelegate contextCreationDelegate;
        List<SongLoader> songLoaders;
#if NORCO_EXCLUDE_DEFAULT_BACKENDS
        contextCreationDelegate = await PfModuleUtility.GetContextDelegateFromDefaultLocationsAsync();
        songLoaders = await PfModuleUtility.GetSongLoadersFromDefaultLocationsAsync();
#else
        contextCreationDelegate = Playful.OpenTK.MPlayerOpenALContext.Create;
        songLoaders = PfModuleUtility.GetLegacySongLoaders();
#endif
        using NorcoManager nm = new(new NorcoOptions(listenPort, showDebug, showCacheInfo), contextCreationDelegate, songLoaders);
        await nm.ExecuteAsync();
        return 0;
    }
}
