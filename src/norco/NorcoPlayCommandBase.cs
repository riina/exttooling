using System.CommandLine;
using System.Reflection;
using Playful;

namespace norco;

public sealed class NorcoPlayCommandBase
{
    private readonly Option<ushort?> _listenPortOption;
    private readonly Option<FileInfo> _playlistOption;
    private readonly Option<bool> _showDebugOption;
    private readonly Option<bool> _showCacheOption;
    private readonly Option<string> _contextReferenceName;
    private readonly Option<string> _contextTypeName;
    private readonly Option<string> _displayReferenceName;
    private readonly Option<string> _displayTypeName;

    public NorcoPlayCommandBase()
    {
        _listenPortOption = new Option<ushort?>("-l", "--listen") { HelpName = "listen port", Description = "Listen port" };
        _playlistOption = new Option<FileInfo>("-p", "--playlist") { HelpName = "playlist", Description = "Playlist to play" };
        _showDebugOption = new Option<bool>("--show-debug") { Description = "Show debug output" };
        _showCacheOption = new Option<bool>("--show-cache") { Description = "Show cache info" };
        _contextReferenceName = new Option<string>("--context-reference-name") { Description = $"Reference name for {nameof(IPlayerContext)} implementation" };
        _contextTypeName = new Option<string>("--context-type-name") { Description = $"Type name for {nameof(IPlayerContext)} implementation" };
        _displayReferenceName = new Option<string>("--display-reference-name") { Description = $"Reference name for {nameof(IPlayerDisplay)} implementation" };
        _displayTypeName = new Option<string>("--display-type-name") { Description = $"Type name for {nameof(IPlayerDisplay)} implementation" };
    }

    public void AddToCommand(Command command)
    {
        command.Add(_listenPortOption);
        command.Add(_playlistOption);
        command.Add(_showDebugOption);
        command.Add(_showCacheOption);
        command.Add(_contextReferenceName);
        command.Add(_contextTypeName);
        command.Add(_displayReferenceName);
        command.Add(_displayTypeName);
    }

    private MPlayerContextCreationDelegate SelectContextCreationAndDisposeUnusedModules(
        ParseResult parseResult,
        List<PfModuleInfo<PfDelegateWithType<MPlayerContextCreationDelegate>>> list,
        out PfModuleInfo<PfDelegateWithType<MPlayerContextCreationDelegate>> selectedModule)
    {
        List<Predicate<Type>> typeFilters = [];
        if (parseResult.GetValue(_contextReferenceName) is { } contextReferenceName)
        {
            typeFilters.Add(v => v.GetCustomAttribute<ReferenceNameAttribute>() is { } rna && string.Equals(rna.Name, contextReferenceName, StringComparison.InvariantCultureIgnoreCase));
        }
        if (parseResult.GetValue(_contextTypeName) is { } contextTypeName)
        {
            typeFilters.Add(v => string.Equals(v.FullName, contextTypeName, StringComparison.InvariantCultureIgnoreCase));
        }
        if (typeFilters.Count == 0)
        {
            typeFilters.Add(v => v.GetCustomAttribute<ExplicitReferenceOnlyAttribute>() == null);
        }
        return SelectAndDisposeUnusedModules(typeFilters, list, out selectedModule);
    }

    private MPlayerDisplayCreationDelegate SelectDisplayCreationAndDisposeUnusedModules(
        ParseResult parseResult,
        List<PfModuleInfo<PfDelegateWithType<MPlayerDisplayCreationDelegate>>> list,
        out PfModuleInfo<PfDelegateWithType<MPlayerDisplayCreationDelegate>> selectedModule)
    {
        List<Predicate<Type>> typeFilters = [];
        if (parseResult.GetValue(_displayReferenceName) is { } contextTypeName)
        {
            typeFilters.Add(v => v.GetCustomAttribute<ReferenceNameAttribute>() is { } rna && string.Equals(rna.Name, contextTypeName, StringComparison.InvariantCultureIgnoreCase));
        }
        if (parseResult.GetValue(_displayTypeName) is { } displayTypeName)
        {
            typeFilters.Add(v => string.Equals(v.FullName, displayTypeName, StringComparison.InvariantCultureIgnoreCase));
        }
        if (typeFilters.Count == 0)
        {
            typeFilters.Add(v => v.GetCustomAttribute<ExplicitReferenceOnlyAttribute>() == null);
        }
        return SelectAndDisposeUnusedModules(typeFilters, list, out selectedModule);
    }

    private static T SelectAndDisposeUnusedModules<T>(
        List<Predicate<Type>> typeFilters,
        List<PfModuleInfo<PfDelegateWithType<T>>> list,
        out PfModuleInfo<PfDelegateWithType<T>> selectedModule
    ) where T : Delegate
    {
        T? result = null;
        selectedModule = null!;
        for (int i = 0; i < list.Count; i++)
        {
            PfModuleInfo<PfDelegateWithType<T>> entry = list[i];
            foreach (var component in entry.GetComponents())
            {
                var componentType = component.Type;
                if (typeFilters.All(v => v(componentType)))
                {
                    result = component.Delegate;
                    selectedModule = entry;
                    break;
                }
            }
            if (result != null)
            {
                if (i + 1 == list.Count)
                {
                    list.RemoveAt(i);
                }
                else
                {
                    list[i] = list[^1];
                    list[^1] = null!;
                    list.RemoveAt(list.Count - 1);
                }
                break;
            }
        }
        foreach (var entry in list)
        {
            entry.Dispose();
        }
        if (result == null)
        {
            throw new InvalidOperationException($"Could not find matching component of type {typeof(T)}");
        }
        return result;
    }

    public async Task<int> RunAsync(ParseResult parseResult)
    {
        ushort? listenPort = parseResult.GetValue(_listenPortOption);
        FileInfo? playlist = parseResult.GetValue(_playlistOption);
        bool showDebug = parseResult.GetValue(_showDebugOption);
        bool showCacheInfo = parseResult.GetValue(_showCacheOption);
        MPlayerContextCreationDelegate contextCreationDelegate;
        MPlayerDisplayCreationDelegate displayCreationDelegate;
        List<SongLoader> songLoaders;
        List<PfModuleInfo> loadedModules = [];
#if NORCO_EXCLUDE_DEFAULT_BACKENDS
        var contextDelegates = await PfModuleUtility.GetContextDelegatesFromDefaultLocationsAsync();
        contextCreationDelegate = SelectContextCreationAndDisposeUnusedModules(parseResult, contextDelegates, out var selectedContextModule);
        loadedModules.Add(selectedContextModule);
        var displayDelegates = await PfModuleUtility.GetDisplayDelegatesFromDefaultLocationsAsync();
        displayCreationDelegate = SelectDisplayCreationAndDisposeUnusedModules(parseResult, displayDelegates, out var selectedDisplayModule);
        loadedModules.Add(selectedDisplayModule);
        var songLoaders2 = await PfModuleUtility.GetSongLoadersFromDefaultLocationsAsync();
        songLoaders = songLoaders2.SelectMany(static v => v.GetComponents()).ToList();
        loadedModules.AddRange(songLoaders2);
#else
        contextCreationDelegate = Playful.OpenTK.OpenALPlayerContext.Create;
        displayCreationDelegate = Playful.StyledConsoleDisplay.StyledConsolePlayerDisplay.Create;
        songLoaders = PfModuleUtility.GetLegacySongLoaders();
#endif
        using NorcoManager nm = new(
            new NorcoOptions(listenPort,
                showDebug,
                showCacheInfo),
            contextCreationDelegate,
            displayCreationDelegate,
            songLoaders,
            loadedModules
        );
        await nm.ExecuteAsync();
        return 0;
    }
}
