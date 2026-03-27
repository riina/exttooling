using System.Reflection;
using Artcore;
using Playful;

namespace norco;

internal static class PfModuleUtility
{
    const string contextSearchConfigFilePattern = "*.pf_context_search_config.json";
    const string displaySearchConfigFilePattern = "*.pf_display_search_config.json";
    const string songLoaderSearchConfigFilePattern = "*.pf_songloader_search_config.json";

    public static Task<List<PfModuleInfo<PfDelegateWithType<MPlayerContextCreationDelegate>>>> GetContextDelegatesFromDefaultLocationsAsync(CancellationToken cancellationToken = default)
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        List<string> searchConfigFiles = [];
        if (new DirectoryInfo(baseDirectory) is { Exists: true } baseDirectoryForSearch)
        {
            searchConfigFiles.AddRange(baseDirectoryForSearch.GetFiles(contextSearchConfigFilePattern).Select(static v => v.FullName));
        }
        return GetContextDelegatesAsync(searchConfigFiles, cancellationToken);
    }

    public static Task<List<PfModuleInfo<PfDelegateWithType<MPlayerDisplayCreationDelegate>>>> GetDisplayDelegatesFromDefaultLocationsAsync(CancellationToken cancellationToken = default)
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        List<string> searchConfigFiles = [];
        if (new DirectoryInfo(baseDirectory) is { Exists: true } baseDirectoryForSearch)
        {
            searchConfigFiles.AddRange(baseDirectoryForSearch.GetFiles(displaySearchConfigFilePattern).Select(static v => v.FullName));
        }
        return GetDisplayDelegatesAsync(searchConfigFiles, cancellationToken);
    }

    public static Task<List<PfModuleInfo<SongLoader>>> GetSongLoadersFromDefaultLocationsAsync(CancellationToken cancellationToken = default)
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        List<string> searchConfigFiles = [];
        if (new DirectoryInfo(baseDirectory) is { Exists: true } baseDirectoryForSearch)
        {
            searchConfigFiles.AddRange(baseDirectoryForSearch.GetFiles(songLoaderSearchConfigFilePattern).Select(static v => v.FullName));
        }
        return GetSongLoadersAsync(searchConfigFiles, cancellationToken);
    }

    public static async Task<List<PfModuleInfo<PfDelegateWithType<MPlayerContextCreationDelegate>>>> GetContextDelegatesAsync(List<string> searchConfigFiles, CancellationToken cancellationToken = default)
    {
        var moduleProvider = new AggregateModuleProvider<ALCModule>(
            await ModuleSearchConfigurationUtility.GetModuleProvidersByPathsAsync(
                ModuleLoadConfiguration.Create(isCollectible: true, passthroughAssemblies: "Playful"),
                searchConfigFiles, cancellationToken));
        var results = new List<PfModuleInfo<PfDelegateWithType<MPlayerContextCreationDelegate>>>();
        foreach (var location in moduleProvider.LoadModuleLocations())
        {
            var module = moduleProvider.LoadModule(location);
            var creatableTypes = GetCreatableTypes<IPlayerContext>(module.Assembly);
            results.Add(new PfModuleInfo<PfDelegateWithType<MPlayerContextCreationDelegate>>(
                module,
                creatableTypes.Select(static v
                    => new PfDelegateWithType<MPlayerContextCreationDelegate>(v.Type, new MPlayerContextCreationDelegate(v.CreationFunc))).ToList()
            ));
        }
        return results;
    }

    public static async Task<List<PfModuleInfo<PfDelegateWithType<MPlayerDisplayCreationDelegate>>>> GetDisplayDelegatesAsync(List<string> searchConfigFiles, CancellationToken cancellationToken = default)
    {
        var moduleProvider = new AggregateModuleProvider<ALCModule>(
            await ModuleSearchConfigurationUtility.GetModuleProvidersByPathsAsync(
                ModuleLoadConfiguration.Create(isCollectible: true, passthroughAssemblies: "Playful"),
                searchConfigFiles, cancellationToken));
        var results = new List<PfModuleInfo<PfDelegateWithType<MPlayerDisplayCreationDelegate>>>();
        foreach (var location in moduleProvider.LoadModuleLocations())
        {
            var module = moduleProvider.LoadModule(location);
            var creatableTypes = GetCreatableTypes<IPlayerDisplay>(module.Assembly);
            results.Add(new PfModuleInfo<PfDelegateWithType<MPlayerDisplayCreationDelegate>>(
                module,
                creatableTypes.Select(static v
                    => new PfDelegateWithType<MPlayerDisplayCreationDelegate>(v.Type, new MPlayerDisplayCreationDelegate(v.CreationFunc))).ToList()
            ));
        }
        return results;
    }

    public static async Task<List<PfModuleInfo<SongLoader>>> GetSongLoadersAsync(List<string> searchConfigFiles, CancellationToken cancellationToken = default)
    {
        var moduleProvider = new AggregateModuleProvider<ALCModule>(
            await ModuleSearchConfigurationUtility.GetModuleProvidersByPathsAsync(
                ModuleLoadConfiguration.Create(isCollectible: true, passthroughAssemblies: "Playful"),
                searchConfigFiles, cancellationToken));
        var results = new List<PfModuleInfo<SongLoader>>();
        foreach (var location in moduleProvider.LoadModuleLocations())
        {
            var module = moduleProvider.LoadModule(location);
            results.Add(new PfModuleInfo<SongLoader>(
                module,
                GetCreatableTypes<SongLoader>(module.Assembly).Select(static v => v.CreationFunc()).ToList()
            ));
        }
        return results;
    }

    public record struct CreatableTypeInfo<T>(Type Type, Func<T> CreationFunc);

    public static List<CreatableTypeInfo<T>> GetCreatableTypes<T>(Assembly assembly)
    {
        var results = new List<CreatableTypeInfo<T>>();
        foreach (var type in assembly.GetExportedTypes())
        {
            if (typeof(T).IsAssignableFrom(type) && !type.IsAbstract && type.GetConstructor([]) != null)
            {
                results.Add(new CreatableTypeInfo<T>(type, () => (T)Activator.CreateInstance(type)!));
            }
        }
        return results;
    }

    public static List<SongLoader> GetLegacySongLoaders()
    {
        var songLoaders = new Dictionary<string, SongLoader>();
        foreach (string assemblyName in NorcoAssemblyNames.GetNames())
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.Load(assemblyName);
            }
            catch
            {
                continue;
            }
            foreach (Type type in assembly.GetExportedTypes()
                         .Where(t => t.IsAssignableTo(typeof(SongLoader)) && !t.IsAbstract && t.GetConstructor(Array.Empty<Type>()) != null))
            {
                try
                {
                    if (type.GetCustomAttribute<SongLoaderInfoAttribute>() is not { } attr || songLoaders.ContainsKey(attr.Name))
                    {
                        continue;
                    }
                    if (Activator.CreateInstance(type) is not SongLoader sl)
                    {
                        continue;
                    }
                    songLoaders.Add(attr.Name, sl);
                }
                catch
                {
                    // ignored
                }
            }
        }
        return songLoaders.Values.ToList();
    }
}
