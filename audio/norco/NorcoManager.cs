using System.Drawing;
using System.Reflection;
using System.Text;
using System.Text.Json;
using EA;
using Playful;

namespace norco;

public sealed class NorcoManager : IDisposable
{
    static NorcoManager()
    {
        s_loaders = new Dictionary<string, SongLoader>();
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
                    if (type.GetCustomAttribute<SongLoaderInfoAttribute>() is not { } attr || s_loaders.ContainsKey(attr.Name)) continue;
                    if (Activator.CreateInstance(type) is not SongLoader sl) continue;
                    s_loaders.Add(attr.Name, sl);
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    private static Dictionary<string, SongLoader> s_loaders;

    private readonly PlaylistManager _playlistManager;
    private volatile int _pendingChanges;
    private Guid _playlistSelector;

    private Guid _currentPlaylistGuid;
    private Playlist _currentPlaylist;
    private Point _xy;

    public NorcoManager()
    {
        _playlistManager = new PlaylistManager(PlaylistManagerOnUpdatedPlaylist, PlaylistManagerChanged);
        _currentPlaylist = new Playlist("", Array.Empty<JsonElement>());
    }

    public async Task ExecuteAsync()
    {
        Console.CursorVisible = false;
        UpdatePlaylistSelector();
        DrawPlaylistScreen();
        while (true)
        {
            Point xy = new(Console.WindowWidth, Console.WindowHeight);
            if (_xy != xy)
            {
                UpdatePlaylistSelector();
                DrawPlaylistScreen();
                _xy = xy;
            }
            else if (Console.KeyAvailable)
            {
                ConsoleKeyInfo cki = Console.ReadKey(true);
                switch (cki.Key)
                {
                    case ConsoleKey.Q:
                        return;
                    case ConsoleKey.UpArrow:
                        {
                            Guid guid = _playlistManager.NameMapping.Select(v => v.Value.Id).TakeWhile(v => v != _playlistSelector).LastOrDefault();
                            _playlistSelector = guid != default ? guid : _playlistManager.NameMapping.Select(v => v.Value.Id).FirstOrDefault();
                            break;
                        }
                    case ConsoleKey.DownArrow:
                        {
                            Guid guid = _playlistManager.NameMapping.Select(v => v.Value.Id).SkipWhile(v => v != _playlistSelector).Skip(1).FirstOrDefault();
                            _playlistSelector = guid != default ? guid : _playlistManager.NameMapping.Select(v => v.Value.Id).LastOrDefault();
                            break;
                        }
                    case ConsoleKey.Enter:
                        {
                            if (!_playlistManager.TryLoadPlaylist(_playlistSelector, out Playlist? playlist)) break;
                            _currentPlaylistGuid = _playlistSelector;
                            _currentPlaylist = playlist;
                            List<MSong> songs = new();
                            foreach (JsonElement x in playlist.Items) songs.AddRange(GetSongs(x));
                            if (!songs.Any()) break;
                            using MPlayer mp = new();
                            foreach (MSong song in songs)
                                mp.Add(song);
                            await mp.ExecuteAsync();
                            break;
                        }
                }
                UpdatePlaylistSelector();
                DrawPlaylistScreen();
            }
            else if (Interlocked.Exchange(ref _pendingChanges, 0) != 0)
            {
                UpdatePlaylistSelector();
                DrawPlaylistScreen();
            }
            else await Task.Delay(10);
        }
    }

    private IEnumerable<MSong> GetSongs(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String && item.GetString() is { } itemStr)
        {
            Uri uri = new(itemStr);
            List<MSong> songs = new();
            using FileStream fs = File.OpenRead(uri.LocalPath);
            foreach (SongLoader loader in s_loaders.Values)
            {
                if (loader.TryLoadSongs(fs, uri, out IReadOnlyCollection<MSong>? lSongs) && lSongs.Any())
                    songs.AddRange(lSongs);
            }
            return songs;
        }
        return Array.Empty<MSong>();
    }

    private void UpdatePlaylistSelector()
    {
        Guid guid = _playlistManager.NameMapping.Select(v => v.Value.Id).FirstOrDefault(v => v == _playlistSelector);
        _playlistSelector = guid != default ? guid : _playlistManager.NameMapping.Select(v => v.Value.Id).FirstOrDefault();
    }

    private void DrawPlaylistScreen()
    {
        if (Console.WindowWidth < 5) return;
        int nameSize = Console.WindowWidth - 3 - 2;
        Console.Clear();
        int index = _playlistSelector == default ? 0 : _playlistManager.NameMapping.Select(v => v.Value.Id).TakeWhile(v => v != _playlistSelector).Count();
        int skip = 0;
        int h = Console.WindowHeight - 1, h2 = h / 2;
        int c = _playlistManager.NameMapping.Count;
        if (index > h2 && c > h)
        {
            skip = Math.Min(c - h, index - h2);
        }
        foreach ((_, (Guid id, string name)) in _playlistManager.NameMapping.Skip(skip).Take(h))
        {
            Console.Write(id == _playlistSelector ? " > " : "   ");
            Console.WriteLine(GetCappedString(name, nameSize));
        }
    }

    private string GetCappedString(string text, int width)
    {
        if (EastAsianWidth.GetWidth(text) <= width) return text;
        StringBuilder sb = new();
        for (int i = 0; width > 0 && i < text.Length; i++)
        {
            char c = text[i];
            int w;
            if (char.IsLowSurrogate(c)) break;
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 == text.Length) break;
                w = EastAsianWidth.GetWidthOfCodePoint(text, i);
                if (w > width)
                {
                    break;
                }
                sb.Append(c).Append(text[++i]);
                width -= w;
            }
            else
            {
                w = EastAsianWidth.GetWidthOfCodePoint(text, i);
                if (w > width)
                {
                    break;
                }
                sb.Append(c);
                width -= w;
            }
        }
        return sb.ToString();
    }


    private void PlaylistManagerOnUpdatedPlaylist(Guid guid, string file, string sub, string name, string info)
    {
        //Console.WriteLine($"{file} {sub} {info}");
    }

    private void PlaylistManagerChanged()
    {
        Interlocked.Increment(ref _pendingChanges);
    }

    public void Dispose() => _playlistManager.Dispose();
}
