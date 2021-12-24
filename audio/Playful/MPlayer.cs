using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using EA;

namespace Playful;

public sealed class MPlayer : IDisposable, IList<MSong>
{
    private const int CrapGap = 33;
    private const int MaxBoxWidth = 50;
    private readonly TaggedPlaylist _songs;
    private readonly MPlayerContext _mPlayerContext;
    private readonly AutoResetEvent _are;
    private Stopwatch _sw;
    private Point _xy;
    private int _index;
    private int _nameScroll;
    private int _albumScroll;
    private int _artistScroll;
    //private int _scrollCtr;

    public MPlayer()
    {
        _mPlayerContext = new MPlayerContext();
        _songs = new TaggedPlaylist();
        _are = new AutoResetEvent(true);
        _sw = new Stopwatch();
    }

    public async Task ExecuteAsync()
    {
        Console.CursorVisible = false;
        _sw.Start();
        _are.WaitOne();
        _index = 0;
        _are.Set();
        while (true)
        {
            MSong song;
            Guid guid;
            _are.WaitOne();
            try
            {
                if (_index >= _songs.Count) break;
                _index = Math.Max(_index, 0);
                song = _songs[_index];
                guid = _songs.Guids[_index];
            }
            finally
            {
                _are.Set();
            }
            using MPlayerOutput p = _mPlayerContext.Stream(song.GetGenerator());
            await p.PlayAsync();
            _nameScroll = 0;
            _albumScroll = 0;
            _artistScroll = 0;
            int vec = 0;
            while (true)
            {
                bool playing = p.PlayState == PlayState.Playing;
                bool setPlaying = playing;
                DrawUpdate(song, _songs.IndexOfGuid(guid), _songs.Count, Math.Clamp(p.TimeApprox / p.Duration, 0, 1), p.Duration, playing);
                await Task.Delay(10);
                if (p.PlayState == PlayState.Ended) break;
                int transport = 0;
                bool spaceLast = false;
                while (Console.KeyAvailable)
                {
                    ConsoleKeyInfo cki = Console.ReadKey(true);
                    switch (cki.Key)
                    {
                        case ConsoleKey.N:
                            vec = -1;
                            break;
                        case ConsoleKey.M:
                            vec = 1;
                            break;
                        case ConsoleKey.LeftArrow:
                            spaceLast = false;
                            transport -= 5;
                            break;
                        case ConsoleKey.RightArrow:
                            spaceLast = false;
                            transport += 5;
                            break;
                        case ConsoleKey.Spacebar:
                            spaceLast = true;
                            setPlaying ^= true;
                            break;
                        case ConsoleKey.Q:
                            return;
                    }
                    if (vec != 0) break;
                }
                if (vec != 0)
                {
                    if (vec == -1)
                    {
                        if (p.TimeApprox < 2.0)
                            vec--;
                    }
                    else
                        vec--;
                    break;
                }
                if (!setPlaying && playing && spaceLast) p.Stop();
                if (transport != 0 || setPlaying && !playing) await p.PlaySeekAsync(transport);
            }
            try
            {
                _index = _songs.IndexOfGuid(guid) + 1 + vec;
            }
            finally
            {
                _are.Set();
            }
        }
        Console.Clear();
    }

    private void DrawUpdate(MSong song, int i, int c, double percent, double duration, bool playing)
    {
        Point xy = new(Console.WindowWidth, Console.WindowHeight);
        if (_xy != xy)
        {
            Console.Clear();
            _xy = xy;
        }
        int my = Math.Max(xy.Y / 2 - 3, 0);
        int boxSize = Math.Min(MaxBoxWidth, xy.X);
        if (boxSize <= 4) return;
        if (_sw.Elapsed.TotalSeconds >= 0.1)
        {
            MoveScroll(song.Name, boxSize, ref _nameScroll, CrapGap);
            MoveScroll(song.Album, boxSize, ref _albumScroll, CrapGap);
            MoveScroll(song.Artist, boxSize, ref _artistScroll, CrapGap);
            _sw.Restart();
        }
        int left = (xy.X - boxSize) / 2;
        TimeSpan elapsed = TimeSpan.FromSeconds(percent * duration);
        TimeSpan total = TimeSpan.FromSeconds(duration);
        if (my < xy.Y)
            WriteLine(left, my++, boxSize, '┌', '┐', '─', $"{i + 1}/{c}", $"{elapsed:mm\\:ss}/{total:mm\\:ss}");
        if (my < xy.Y)
            WriteBox(left, my++, boxSize, '│', '│', song.Name, _nameScroll, CrapGap);
        if (my < xy.Y)
            WriteBox(left, my++, boxSize, '│', '│', song.Album, _albumScroll, CrapGap);
        if (my < xy.Y)
            WriteBox(left, my++, boxSize, '│', '│', song.Artist, _artistScroll, CrapGap);
        if (my < xy.Y)
            WriteProgressBox(left, my++, boxSize, '└', '┘', '─', playing ? '*' : '@', percent);
        if (my < xy.Y)
            WriteLine(left, my, boxSize, ' ', ' ', ' ', $"</>:jmp n/m:prev/next space:play/pause q:ex");
    }

    private static void WriteBox(int left, int top, int boxSize, char l, char r, string text, int scroll, int loopGap)
    {
        StringBuilder sb = new();
        sb.Append(l);
        int eaw = EastAsianWidth.GetWidth(text);
        if (eaw <= boxSize - 2)
        {
            int ww = boxSize - 2 - eaw;
            int ll = ww / 2;
            sb.Append(' ', ll);
            sb.Append(text);
            sb.Append(' ', ww - ll);
        }
        else
        {
            PopulateWidth(sb, text, boxSize - 2, scroll, loopGap);
        }
        sb.Append(r);
        Console.CursorLeft = left;
        Console.CursorTop = top;
        Console.Write(sb.ToString());
    }

    private static void WriteLine(int left, int top, int boxSize, char l, char r, char fill)
    {
        StringBuilder sb = new();
        sb.Append(l);
        sb.Append(fill, boxSize - 2);
        sb.Append(r);
        Console.CursorLeft = left;
        Console.CursorTop = top;
        Console.Write(sb.ToString());
    }

    private static void WriteLine(int left, int top, int boxSize, char l, char r, char fill, string text)
    {
        int eaw = EastAsianWidth.GetWidth(text);
        if (eaw <= boxSize - 2)
        {
            int ff = boxSize - 2 - eaw;
            int ll = ff / 2;
            StringBuilder sb = new();
            sb.Append(l);
            sb.Append(fill, ll);
            sb.Append(text);
            sb.Append(fill, ff - ll);
            sb.Append(r);
            Console.CursorLeft = left;
            Console.CursorTop = top;
            Console.Write(sb.ToString());
        }
        else
        {
            WriteLine(left, top, boxSize, l, r, fill);
        }
    }

    private static void WriteLine(int left, int top, int boxSize, char l, char r, char fill, string textL, string textR)
    {
        int eawL = EastAsianWidth.GetWidth(textL);
        int eawR = EastAsianWidth.GetWidth(textR);
        if (eawL + eawR <= boxSize - 2)
        {
            int ff = boxSize - 2 - eawL - eawR;
            StringBuilder sb = new();
            sb.Append(l);
            sb.Append(textL);
            sb.Append(fill, ff);
            sb.Append(textR);
            sb.Append(r);
            Console.CursorLeft = left;
            Console.CursorTop = top;
            Console.Write(sb.ToString());
        }
        else
        {
            WriteLine(left, top, boxSize, l, r, fill);
        }
    }


    private static void WriteProgressBox(int left, int top, int boxSize, char l, char r, char fill, char playHead, double percent)
    {
        if (EastAsianWidth.IsFullwidthOrWide(fill)) throw new ArgumentException();
        if (EastAsianWidth.IsFullwidthOrWide(playHead)) throw new ArgumentException();
        StringBuilder sb = new();
        sb.Append(l);
        boxSize -= 2;
        double cPercent = 1.0 / boxSize;
        bool first = true;
        for (int i = 0; i < boxSize; i++)
        {
            percent -= cPercent;
            if (percent < 0)
            {
                if (first)
                {
                    first = false;
                    sb.Append(playHead);
                }
                else
                    sb.Append(' ');
            }
            else
                sb.Append(fill);
        }
        sb.Append(r);
        Console.CursorLeft = left;
        Console.CursorTop = top;
        Console.Write(sb.ToString());
    }

    /*private static bool DoScroll(ref int ctr, int ctrMax)
    {
        ctr++;
        if (ctr == ctrMax) ctr = 0;
        else return false;
        return true;
    }*/

    private static void MoveScroll(string text, int boxSize, ref int scroll, int loopGap)
    {
        if (EastAsianWidth.GetWidth(text) <= boxSize - 2)
        {
            scroll = 0;
            return;
        }
        if (scroll >= text.Length + loopGap - 1) scroll = 0;
        else if (scroll >= text.Length) scroll++;
        else if (char.IsHighSurrogate(text[scroll])) scroll += 2;
        else scroll += 1;
    }

    private static void PopulateWidth(StringBuilder sb, string text, int width, int scroll, int loopGap)
    {
        int i = scroll;
        while (width > 0)
        {
            for (; width > 0 && i < text.Length; i++)
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
                        sb.Append('…');
                        width--;
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
                        sb.Append('…');
                        width--;
                        break;
                    }
                    sb.Append(c);
                    width -= w;
                }
            }
            i = 0;
            int max;
            if (scroll >= text.Length)
            {
                max = loopGap - (scroll - text.Length);
                scroll = 0;
            }
            else
            {
                max = loopGap;
            }
            for (int j = 0; width > 0 && j < max; j++)
            {
                sb.Append(' ');
                width--;
            }
        }
    }

    public void Dispose() => _mPlayerContext.Dispose();

    public IEnumerator<MSong> GetEnumerator() => _songs.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_songs).GetEnumerator();

    public void Add(MSong item) => _songs.Add(item);

    public void Clear() => _songs.Clear();

    public bool Contains(MSong item) => _songs.Contains(item);

    public void CopyTo(MSong[] array, int arrayIndex) => _songs.CopyTo(array, arrayIndex);

    public bool Remove(MSong item) => _songs.Remove(item);

    public int Count => _songs.Count;

    public bool IsReadOnly => _songs.IsReadOnly;

    public int IndexOf(MSong item) => _songs.IndexOf(item);

    public void Insert(int index, MSong item) => _songs.Insert(index, item);

    public void RemoveAt(int index) => _songs.RemoveAt(index);

    public MSong this[int index]
    {
        get => _songs[index];
        set => _songs[index] = value;
    }
}
