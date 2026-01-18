using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using EA;
using Playful;

namespace norco;

public sealed class NorcoManager : IDisposable
{
    private readonly NorcoOptions _options;
    private const string Header = "</>:jmp n/m:prev/next space:play/pause q:ex";

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
    private TcpListener? _tcp;

    public NorcoManager(NorcoOptions options)
    {
        _options = options;
        _playlistManager = new PlaylistManager(PlaylistManagerOnUpdatedPlaylist, PlaylistManagerChanged);
        _currentPlaylist = new Playlist("", Array.Empty<JsonElement>());
        if (_options.ListenPort is { } listenPort)
        {
            _tcp = new TcpListener(new IPEndPoint(IPAddress.Loopback, listenPort));
            // TODO handle incoming connections with DisplayStateWriter
            _tcp?.Start();
        }
    }

    private enum ControlKey
    {
        Q,
        UpArrow,
        DownArrow,
        Enter,
        N,
        M,
        LeftArrow,
        RightArrow,
        Spacebar

    }

    private interface IControlBackend
    {
        ControlKey? GetNextInput();
    }

    private class BasicControlBackend : IControlBackend
    {
        public ControlKey? GetNextInput()
        {
            if (Console.KeyAvailable)
            {
                return Console.ReadKey(true).Key switch
                {
                    ConsoleKey.Q => ControlKey.Q,
                    ConsoleKey.UpArrow => ControlKey.UpArrow,
                    ConsoleKey.DownArrow => ControlKey.DownArrow,
                    ConsoleKey.Enter => ControlKey.Enter,
                    ConsoleKey.N => ControlKey.N,
                    ConsoleKey.M => ControlKey.M,
                    ConsoleKey.LeftArrow => ControlKey.LeftArrow,
                    ConsoleKey.RightArrow => ControlKey.RightArrow,
                    ConsoleKey.Spacebar => ControlKey.Spacebar,
                    _ => null
                };
            }
            return null;
        }
    }

    private class AltControlBackend : IControlBackend
    {
        public ControlKey? GetNextInput()
        {
            if (Console.In.Peek() >= 0)
            {
                return char.ToLowerInvariant((char)Console.Read()) switch
                {
                    'q' => ControlKey.Q,
                    'w' => ControlKey.UpArrow,
                    's' => ControlKey.DownArrow,
                    ';' => ControlKey.Enter,
                    'n' => ControlKey.N,
                    'm' => ControlKey.M,
                    'a' => ControlKey.LeftArrow,
                    'd' => ControlKey.RightArrow,
                    'p' => ControlKey.Spacebar,
                    _ => null
                };
            }
            return null;
        }
    }

    public async Task ExecuteAsync()
    {
        Console.CursorVisible = false;
        UpdatePlaylistSelector();
        DrawPlaylistScreen();
        IControlBackend controlBackend = Console.IsInputRedirected ? new AltControlBackend() : new BasicControlBackend();
        while (true)
        {
            Point xy = new(Console.WindowWidth, Console.WindowHeight);
            if (_xy != xy)
            {
                UpdatePlaylistSelector();
                DrawPlaylistScreen();
                _xy = xy;
            }
            else if (controlBackend.GetNextInput() is { } mainControlKey)
            {
                switch (mainControlKey)
                {
                    case ControlKey.Q:
                        return;
                    case ControlKey.UpArrow:
                        {
                            Guid guid = _playlistManager.NameMapping.Select(v => v.Value.Id).TakeWhile(v => v != _playlistSelector).LastOrDefault();
                            _playlistSelector = guid != default ? guid : _playlistManager.NameMapping.Select(v => v.Value.Id).FirstOrDefault();
                            break;
                        }
                    case ControlKey.DownArrow:
                        {
                            Guid guid = _playlistManager.NameMapping.Select(v => v.Value.Id).SkipWhile(v => v != _playlistSelector).Skip(1).FirstOrDefault();
                            _playlistSelector = guid != default ? guid : _playlistManager.NameMapping.Select(v => v.Value.Id).LastOrDefault();
                            break;
                        }
                    case ControlKey.Enter:
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
                            CancellationTokenSource mpts = new();
                            Task t = mp.StartExecuteAsync(mpts.Token);
                            MPlayerDisplay display = new();
                            Task dt = Task.Run(async () =>
                            {
                                using MPlayerDisplay mpd = display;
                                await mpd.ExecuteAsync(mpts.Token);
                            }, mpts.Token);
                            bool playing = true;
                            while (true)
                            {
                                if (mp.Ended || !mp.Active) break;
                                if (dt.IsFaulted) throw dt.Exception!;
                                if (t.IsFaulted) throw t.Exception!;
                                bool setPlaying = playing;
                                if (mp.TryGetDisplayState(out MPlayerDisplayState displayState))
                                    display.SetDisplayState(displayState with { Message = Header });
                                await Task.Delay(10, default);
                                int transport = 0;
                                bool spaceLast = false;
                                int vec = 0;
                                while (controlBackend.GetNextInput() is { } subControlKey)
                                {
                                    switch (subControlKey)
                                    {
                                        case ControlKey.N:
                                            vec = -1;
                                            break;
                                        case ControlKey.M:
                                            vec = 1;
                                            break;
                                        case ControlKey.LeftArrow:
                                            spaceLast = false;
                                            transport -= 5;
                                            break;
                                        case ControlKey.RightArrow:
                                            spaceLast = false;
                                            transport += 5;
                                            break;
                                        case ControlKey.Spacebar:
                                            spaceLast = true;
                                            setPlaying ^= true;
                                            break;
                                        case ControlKey.Q:
                                            goto quitPlayer;
                                    }
                                    if (vec != 0) break;
                                }
                                if (vec != 0) mp.SeekTrack(vec);
                                if (transport != 0) await mp.PlaySeekAsync(transport, default);
                                if (setPlaying != playing && spaceLast)
                                {
                                    if (setPlaying) await mp.PlaySeekAsync(transport, default);
                                    else mp.Stop();
                                }
                                playing = setPlaying;
                            }
                            quitPlayer:
                            mpts.Cancel();
                            try
                            {
                                await t;
                            }
                            catch
                            {
                                // ignored
                            }
                            try
                            {
                                await dt;
                            }
                            catch
                            {
                                // ignored
                            }
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

    public void Dispose()
    {
        _playlistManager.Dispose();
        _tcp?.Stop();
    }
}
