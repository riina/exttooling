using System.Diagnostics;
using System.Drawing;
using System.Text;
using EA;

namespace Playful.StyledConsoleDisplay;

public sealed class MPlayerDisplay : IPlayerDisplay
{
    private const int CrapGap = 33;
    private const int MaxBoxWidth = 50;
    public bool ShowDebug { get; set; }
    public bool ShowCacheInfo { get; set; }
    private readonly AutoResetEvent _are;
    private readonly Stopwatch _sw;
    private MPlayerDisplayState _displayState;
    private volatile int _started;
    private bool _disposed;
    private Point _xy;
    private int _nameScroll;
    private int _albumScroll;
    private int _artistScroll;
    private int _debugScroll;
    private RunTask? _displayTask;
    //private int _scrollCtr;

    public static MPlayerDisplay Create()
    {
        return new MPlayerDisplay();
    }

    public MPlayerDisplay()
    {
        _are = new AutoResetEvent(true);
        _sw = new Stopwatch();
        _displayState = new MPlayerDisplayState(
            0,
            0,
            0.0,
            0,
            0,
            0.1,
            PlayState.Stopped,
            "",
            "",
            "",
            null,
            "");
    }

    private record RunTask(CancellationTokenSource Source, Task Task);

    public Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        EnableStartOnce();
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task execute = ExecuteInternalAsync(cts.Token);
        _displayTask = new RunTask(cts, execute);
        return execute;
    }

    private async Task ExecuteInternalAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        Console.CursorVisible = false;
        _sw.Start();
        _nameScroll = 0;
        _albumScroll = 0;
        _artistScroll = 0;
        _debugScroll = 0;
        while (true)
        {
            _are.WaitOne();
            try
            {
                bool playing = _displayState.PlayState == PlayState.Playing;
                DrawUpdate(
                    _displayState.Name,
                    _displayState.Album,
                    _displayState.Artist,
                    _displayState.Debug,
                    _displayState.Message,
                    _displayState.Index,
                    _displayState.Count,
                    Math.Clamp(_displayState.Time / _displayState.Duration,
                        0,
                        1),
                    Math.Clamp(_displayState.TimeCacheStart / _displayState.Duration,
                        0,
                        1),
                    Math.Clamp(_displayState.TimeCacheEnd / _displayState.Duration,
                        0,
                        1),
                    _displayState.Duration,
                    playing);
                await Task.Delay(10, cancellationToken);
            }
            finally
            {
                _are.Set();
            }
        }
    }

    public void SetDisplayState(MPlayerDisplayState displayState)
    {
        EnsureNotDisposed();
        _are.WaitOne();
        _displayState = displayState;
        _are.Set();
    }

    private void DrawUpdate(
        string name,
        string album,
        string artist,
        string? debug,
        string? message,
        int i,
        int c,
        double percent,
        double percentCacheStart,
        double percentCacheEnd,
        double duration,
        bool playing)
    {
        Point xy = new(Console.WindowWidth, Console.WindowHeight);
        if (_xy != xy)
        {
            Console.Clear();
            _xy = xy;
        }
        int my = Math.Max(xy.Y / 2 - 3, 0);
        int boxSize = Math.Min(MaxBoxWidth, xy.X);
        if (boxSize <= 4)
        {
            return;
        }
        if (_sw.Elapsed.TotalSeconds >= 0.1)
        {
            MoveScroll(name, boxSize, ref _nameScroll, CrapGap);
            MoveScroll(album, boxSize, ref _albumScroll, CrapGap);
            MoveScroll(artist, boxSize, ref _artistScroll, CrapGap);
            if (debug != null)
            {
                MoveScroll(debug, boxSize, ref _debugScroll, CrapGap);
            }
            _sw.Restart();
        }
        int left = (xy.X - boxSize) / 2;
        TimeSpan elapsed = TimeSpan.FromSeconds(percent * duration);
        TimeSpan total = TimeSpan.FromSeconds(duration);
        if (my < xy.Y)
        {
            WriteLine(left, my++, boxSize, '╔', '╗', '═', $"{i + 1}/{c}", $"{elapsed:mm\\:ss}/{total:mm\\:ss}");
        }
        if (my < xy.Y)
        {
            WriteBox(left, my++, boxSize, '║', '║', name, _nameScroll, CrapGap);
        }
        if (my < xy.Y)
        {
            WriteBox(left, my++, boxSize, '║', '║', album, _albumScroll, CrapGap);
        }
        if (my < xy.Y)
        {
            WriteBox(left, my++, boxSize, '║', '║', artist, _artistScroll, CrapGap);
        }
        if (ShowDebug && debug != null && my < xy.Y)
        {
            WriteBox(left, my++, boxSize, '║', '║', debug, _debugScroll, CrapGap);
        }
        if (ShowCacheInfo && my < xy.Y)
        {
            WriteCacheBox(
                left,
                my++,
                boxSize,
                '║',
                '║',
                '─',
                ' ',
                ' ',
                ' ',
                percentCacheStart,
                percentCacheEnd);
        }
        if (my < xy.Y)
        {
            WriteProgressBox(
                left,
                my++,
                boxSize,
                '╚',
                '╝',
                '═',
                '─',
                ' ',
                ' ',
                ' ',
                playing
                    ? '>'
                    : '@',
                percent,
                percentCacheStart,
                percentCacheEnd);
        }
        if (my < xy.Y)
        {
            WriteLine(left, my, boxSize, ' ', ' ', ' ', message ?? "");
        }
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

    private static void WriteLine(
        int left,
        int top,
        int boxSize,
        char l,
        char r,
        char fill,
        string textL,
        string textR)
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

    private static void WriteProgressBox(
        int left,
        int top,
        int boxSize,
        char l,
        char r,
        char fill,
        char fillCached,
        char fillPartiallyCachedLeft,
        char fillPartiallyCachedRight,
        char fillNotCached,
        char playHead,
        double percent,
        double percentCacheStart,
        double percentCacheEnd)
    {
        if (EastAsianWidth.IsFullwidthOrWide(fill))
        {
            throw new ArgumentException();
        }
        if (EastAsianWidth.IsFullwidthOrWide(fillCached))
        {
            throw new ArgumentException();
        }
        if (EastAsianWidth.IsFullwidthOrWide(fillPartiallyCachedLeft))
        {
            throw new ArgumentException();
        }
        if (EastAsianWidth.IsFullwidthOrWide(fillPartiallyCachedRight))
        {
            throw new ArgumentException();
        }
        if (EastAsianWidth.IsFullwidthOrWide(fillNotCached))
        {
            throw new ArgumentException();
        }
        if (EastAsianWidth.IsFullwidthOrWide(playHead))
        {
            throw new ArgumentException();
        }
        StringBuilder sb = new();
        sb.Append(l);
        boxSize -= 2;
        double cPercent = 1.0 / boxSize;
        for (int i = 0; i < boxSize; i++)
        {
            double startPercent = cPercent * i, endPercent = cPercent * (i + 1);
            if (percent >= endPercent)
            {
                sb.Append(fill);
            }
            else if (percent < startPercent)
            {
                if (percentCacheEnd <= startPercent || percentCacheStart >= endPercent)
                {
                    sb.Append(fillNotCached);
                }
                else if (percentCacheStart <= startPercent && percentCacheEnd >= endPercent)
                {
                    sb.Append(fillCached);
                }
                else
                {
                    sb.Append(percentCacheStart < startPercent ? fillPartiallyCachedLeft : fillPartiallyCachedRight);
                }
            }
            else
            {
                sb.Append(playHead);
            }
        }
        sb.Append(r);
        Console.CursorLeft = left;
        Console.CursorTop = top;
        Console.Write(sb.ToString());
    }

    private static void WriteCacheBox(
        int left,
        int top,
        int boxSize,
        char l,
        char r,
        char fillCached,
        char fillPartiallyCachedLeft,
        char fillPartiallyCachedRight,
        char fillNotCached,
        double percentCacheStart,
        double percentCacheEnd)
    {
        if (EastAsianWidth.IsFullwidthOrWide(fillCached))
        {
            throw new ArgumentException();
        }
        if (EastAsianWidth.IsFullwidthOrWide(fillPartiallyCachedLeft))
        {
            throw new ArgumentException();
        }
        if (EastAsianWidth.IsFullwidthOrWide(fillPartiallyCachedRight))
        {
            throw new ArgumentException();
        }
        if (EastAsianWidth.IsFullwidthOrWide(fillNotCached))
        {
            throw new ArgumentException();
        }
        StringBuilder sb = new();
        sb.Append(l);
        boxSize -= 2;
        double cPercent = 1.0 / boxSize;
        for (int i = 0; i < boxSize; i++)
        {
            double startPercent = cPercent * i, endPercent = cPercent * (i + 1);
            if (percentCacheEnd <= startPercent || percentCacheStart >= endPercent)
            {
                sb.Append(fillNotCached);
            }
            else if (percentCacheStart <= startPercent && percentCacheEnd >= endPercent)
            {
                sb.Append(fillCached);
            }
            else
            {
                sb.Append(percentCacheStart < startPercent ? fillPartiallyCachedLeft : fillPartiallyCachedRight);
            }
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
        if (scroll >= text.Length + loopGap - 1)
        {
            scroll = 0;
        }
        else if (scroll >= text.Length)
        {
            scroll++;
        }
        else if (char.IsHighSurrogate(text[scroll]))
        {
            scroll += 2;
        }
        else
        {
            scroll += 1;
        }
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
                if (char.IsLowSurrogate(c))
                {
                    break;
                }
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 == text.Length)
                    {
                        break;
                    }
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

    private void EnableStartOnce()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 1)
        {
            throw new InvalidOperationException("Cannot start display more than once");
        }
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MPlayerDisplay));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_displayTask != null)
        {
            _displayTask.Source.Cancel();
            try
            {
                _displayTask.Task.Wait();
            }
            catch
            {
                // ignored
            }
        }
        _are.Dispose();
    }
}
