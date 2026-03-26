using System.Reflection;
using Artcore;
using Playful;

namespace norco;

internal static class PfModuleUtility
{
    const string contextSearchConfigFilePattern = "*.pf_context_search_config.json";
    const string songLoaderSearchConfigFilePattern = "*.pf_songloader_search_config.json";

    public static Task<MPlayerContextCreationDelegate> GetContextDelegateFromDefaultLocationsAsync(CancellationToken cancellationToken = default)
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        List<string> searchConfigFiles = [];
        if (new DirectoryInfo(baseDirectory) is { Exists: true } baseDirectoryForSearch)
        {
            searchConfigFiles.AddRange(baseDirectoryForSearch.GetFiles(contextSearchConfigFilePattern).Select(static v => v.FullName));
        }
        return GetContextDelegateAsync(searchConfigFiles, cancellationToken);
    }

    public static Task<List<SongLoader>> GetSongLoadersFromDefaultLocationsAsync(CancellationToken cancellationToken = default)
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        List<string> searchConfigFiles = [];
        if (new DirectoryInfo(baseDirectory) is { Exists: true } baseDirectoryForSearch)
        {
            searchConfigFiles.AddRange(baseDirectoryForSearch.GetFiles(songLoaderSearchConfigFilePattern).Select(static v => v.FullName));
        }
        return GetSongLoadersAsync(searchConfigFiles, cancellationToken);
    }

    public static async Task<MPlayerContextCreationDelegate> GetContextDelegateAsync(List<string> searchConfigFiles, CancellationToken cancellationToken = default)
    {
        var moduleProvider = new AggregateModuleProvider<ALCModule>(
            await ModuleSearchConfigurationUtility.GetModuleProvidersByPathsAsync(
                ModuleLoadConfiguration.Create(passthroughAssemblies: "Playful"),
                searchConfigFiles, cancellationToken));
        foreach (var location in moduleProvider.LoadModuleLocations())
        {
            var module = moduleProvider.LoadModule(location);
            var creatableTypes = GetCreatableTypes<IPlayerContext>(module.Assembly);
            if (creatableTypes.Count == 0)
            {
                module.AssemblyLoadContext.Unload();
                continue;
            }
            return new MPlayerContextCreationDelegate(creatableTypes[0].CreationFunc);
        }
        throw new InvalidOperationException($"Could not find an {nameof(IPlayerContext)}");
    }

    public static async Task<List<SongLoader>> GetSongLoadersAsync(List<string> searchConfigFiles, CancellationToken cancellationToken = default)
    {
        var moduleProvider = new AggregateModuleProvider<ALCModule>(
            await ModuleSearchConfigurationUtility.GetModuleProvidersByPathsAsync(
                ModuleLoadConfiguration.Create(passthroughAssemblies: "Playful"),
                searchConfigFiles, cancellationToken));
        var songLoaders = new List<SongLoader>();
        foreach (var location in moduleProvider.LoadModuleLocations())
        {
            var module = moduleProvider.LoadModule(location);
            songLoaders.AddRange(GetCreatableTypes<SongLoader>(module.Assembly).Select(static v => v.CreationFunc()));
        }
        return songLoaders;
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
