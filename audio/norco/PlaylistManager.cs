using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace norco;

public sealed class PlaylistManager : IDisposable
{
    static PlaylistManager()
    {
        s_playlistDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PlaylistDirectoryName);
    }

    public const string PlaylistDirectoryName = "playlists";

    private static readonly string s_playlistDirectory;

    public delegate void UpdatedPlaylistDelegate(Guid guid, string file, string sub, string name, string info);

    private readonly string _playlistDirectory;
    private readonly Action? _changeAction;
    private readonly Bijection<Guid, string> _mapping;
    private readonly SortedList<string, (Guid Id, string Name)> _nameMapping;
    public readonly IReadOnlyCollection<KeyValuePair<Guid, string>> Mapping;
    public readonly IReadOnlyCollection<KeyValuePair<string, (Guid Id, string Name)>> NameMapping;
    private readonly FileSystemWatcher _fsw;

    public event UpdatedPlaylistDelegate? UpdatedPlaylist;

    public PlaylistManager(UpdatedPlaylistDelegate? updatedPlaylistDelegate = null, Action? changeAction = null) : this(s_playlistDirectory, updatedPlaylistDelegate, changeAction)
    {
    }

    public PlaylistManager(string playlistDirectory, UpdatedPlaylistDelegate? updatedPlaylistDelegate = null, Action? changeAction = null)
    {
        _playlistDirectory = playlistDirectory;
        _changeAction = changeAction;
        if (!Directory.Exists(playlistDirectory)) Directory.CreateDirectory(playlistDirectory);
        UpdatedPlaylist = updatedPlaylistDelegate;
        _mapping = new Bijection<Guid, string>();
        _nameMapping = new SortedList<string, (Guid Id, string Name)>();
        Queue<string> dirQueue = new();
        dirQueue.Enqueue(playlistDirectory);
        while (dirQueue.TryDequeue(out string? dir))
        {
            foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
            {
                string to = Path.GetFullPath(file);
                if (!TryGetSubPath(to, out string? toFull, out string? toSub)) continue;
                Guid guid = Guid.NewGuid();
                _mapping.Add(guid, to);
                string toName = GetName(toFull);
                _nameMapping.Add(toSub, (guid, toName));
                OnUpdatedPlaylist(guid, toFull, toSub, toName, "created");
            }
            foreach (string d in Directory.GetDirectories(dir))
                dirQueue.Enqueue(d);
        }
        Mapping = _mapping;
        NameMapping = _nameMapping;
        _fsw = new FileSystemWatcher(playlistDirectory, "*.json") { EnableRaisingEvents = true, IncludeSubdirectories = true, NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite };
        _fsw.Changed += FswOnChanged;
        _fsw.Created += FswOnCreated;
        _fsw.Deleted += FswOnDeleted;
        _fsw.Renamed += FswOnRenamed;
        _fsw.IncludeSubdirectories = true;
    }

    public bool TryGetGuid(string path, out Guid guid) => _mapping.TryGetA(path, out guid);

    public bool TryGetPath(Guid guid, [NotNullWhen(true)] out string? path) => _mapping.TryGetB(guid, out path);

    private void FswOnRenamed(object sender, RenamedEventArgs e)
    {
        string from = e.OldFullPath, to = e.FullPath;
        if (!TryGetSubPath(from, out string? fromFull, out string? fromSub)) return;
        if (!_mapping.TryGetA(fromFull, out Guid guid)) return;
        if (!_nameMapping.TryGetValue(fromSub, out var fromName)) return;
        _mapping.RemoveB(fromFull);
        _nameMapping.Remove(fromSub);
        if (!TryGetSubPath(to, out string? toFull, out string? toSub)) return;
        _mapping.RemoveB(toFull);
        _mapping.Add(guid, toFull);
        string toName = fromName.Name;
        _nameMapping.Add(toSub, (guid, toName));
        OnUpdatedPlaylist(guid, toFull, toSub, toName, "renamed");
        _changeAction?.Invoke();
    }

    private void FswOnDeleted(object sender, FileSystemEventArgs e)
    {
        string to = e.FullPath;
        if (!TryGetSubPath(to, out string? toFull, out string? toSub)) return;
        _mapping.RemoveB(toFull);
        _nameMapping.Remove(toSub);
        _changeAction?.Invoke();
    }

    private void FswOnCreated(object sender, FileSystemEventArgs e)
    {
        string to = e.FullPath;
        if (!TryGetSubPath(to, out string? toFull, out string? toSub)) return;
        if (_mapping.ContainsB(toFull)) return;
        Guid guid = Guid.NewGuid();
        _mapping.Add(guid, toFull);
        string toName = GetName(toFull);
        _nameMapping.Add(toSub, (guid, toName));
        OnUpdatedPlaylist(guid, toFull, toSub, toName, "created");
        _changeAction?.Invoke();
    }

    private void FswOnChanged(object sender, FileSystemEventArgs e)
    {
        string to = e.FullPath;
        if (!TryGetSubPath(to, out string? toFull, out string? toSub)) return;
        if (!_mapping.TryGetA(toFull, out Guid guid)) return;
        if (!_nameMapping.TryGetValue(toSub, out var nameMap)) return;
        _nameMapping.Remove(toSub);
        string toName = GetName(toFull);
        _nameMapping[toSub] = (nameMap.Id, toName);
        OnUpdatedPlaylist(guid, toFull, toSub, toName, "changed");
        _changeAction?.Invoke();
    }

    private bool TryGetSubPath(string path, [NotNullWhen(true)] out string? fullPath, [NotNullWhen(true)] out string? subPath)
    {
        path = Path.GetFullPath(path);
        string sub = Path.GetRelativePath(_playlistDirectory, path);
        if (sub == path || !path.EndsWith(sub)) goto fail;
        fullPath = path;
        subPath = sub;
        return true;
        fail:
        fullPath = null;
        subPath = null;
        return false;
    }

    private string GetName(string path)
    {
        try
        {
            if (TryLoadPlaylist(path, out Playlist? playlist) && !string.IsNullOrWhiteSpace(playlist.Name))
                return playlist.Name;
        }
        catch
        {
            // ignored
        }
        return "Unknown Playlist";
    }

    private static readonly JsonSerializerOptions s_jOpts = new() { PropertyNameCaseInsensitive = true };

    public bool TryLoadPlaylist(string path, [NotNullWhen(true)] out Playlist? playlist)
    {
        try
        {
            using FileStream fs = File.OpenRead(path);
            playlist = JsonSerializer.Deserialize<Playlist>(fs, s_jOpts);
            return playlist != null;
        }
        catch
        {
            // ignored
        }
        playlist = null;
        return false;
    }

    public bool TryLoadPlaylist(Guid selection, [NotNullWhen(true)] out Playlist? playlist)
    {
        if (!TryGetPath(selection, out string? path))
        {
            playlist = null;
            return false;
        }
        return TryLoadPlaylist(path, out playlist);
    }

    public void Dispose() => _fsw.Dispose();

    private void OnUpdatedPlaylist(Guid guid, string file, string sub, string name, string info)
    {
        UpdatedPlaylist?.Invoke(guid, file, sub, name, info);
    }
}
