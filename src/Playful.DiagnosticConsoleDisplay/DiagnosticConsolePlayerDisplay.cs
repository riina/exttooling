using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Text;

namespace Playful.DiagnosticConsoleDisplay;

[ReferenceName(nameof(DiagnosticConsolePlayerDisplay))]
[ExplicitReferenceOnly]
public sealed class DiagnosticConsolePlayerDisplay : IPlayerDisplay
{
    public bool ShowDebug { get; set; }
    public bool ShowCacheInfo { get; set; }
    private readonly AutoResetEvent _are;
    private readonly Stopwatch _sw;
    private readonly StringQueueDebugWriter _debugWriter = new();
    private readonly TextWriter _out = Console.Out;
    private MPlayerDisplayState _displayState;
    private volatile int _started;
    private int _contentLineIndex;
    private int _displayLineCount;
    private bool _disposed;
    private RunTask? _displayTask;

    public static DiagnosticConsolePlayerDisplay Create()
    {
        return new DiagnosticConsolePlayerDisplay();
    }

    public DiagnosticConsolePlayerDisplay()
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
        _sw.Start();
        while (true)
        {
            _are.WaitOne();
            try
            {
                var currentCancellationTokenSource = _debugWriter.GetNextCancellationTokenSource(cancellationToken);
                // clear out previously drawn player info before writing
                var writeQueue = new StringBuilder();
                if (_displayLineCount > 0)
                {
                    writeQueue.Append("\e[2K\r");
                    for (int i = 1; i < _displayLineCount; i++)
                    {
                        writeQueue.Append("\e[1A\e[2K\r");
                    }
                }
                _displayLineCount = 0;
                if (_contentLineIndex != 0)
                {
                    writeQueue.Append($"\r\e[1A\e[{_contentLineIndex}C");
                }
                while (_debugWriter.TryDequeue(out string? text))
                {
                    writeQueue.Append(text);
                }
                _out.Write(writeQueue.ToString());
                _out.Flush();
                int currentIndex = Console.CursorLeft;
                _contentLineIndex = currentIndex;
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
                    _displayState.PlayState);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(0.1), currentCancellationTokenSource.Token);
                }
                catch
                {
                    // ignored
                }
                cancellationToken.ThrowIfCancellationRequested();
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
        PlayState playState)
    {
        Point xy = new(Console.WindowWidth, Console.WindowHeight);
        var writeQueue = new StringBuilder();
        if (_contentLineIndex != 0)
        {
            writeQueue.AppendLine();
        }
        _displayLineCount = 1;
        WriteLine(writeQueue, new string('-', xy.X - 1));
        WriteKvp(writeQueue, "Content", $"{artist} - {name}");
        WriteKvp(writeQueue, "Album", album);
        WriteKvp(writeQueue, "Queue", $"{i + 1}/{c}");
        WriteKvp(writeQueue, "Progress", $"{FormatTime(TimeSpan.FromSeconds(duration * percent))}/{FormatTime(TimeSpan.FromSeconds(duration))} ({playState})");
        if (ShowCacheInfo)
        {
            WriteKvp(writeQueue, "Cache", $"{FormatTime(TimeSpan.FromSeconds(duration * percentCacheStart))}/{FormatTime(TimeSpan.FromSeconds(duration * percentCacheEnd))}");
        }
        if (ShowDebug && debug != null)
        {
            WriteKvp(writeQueue, "Debug", debug);
        }
        if (message != null)
        {
            WriteKvp(writeQueue, "Info", message);
        }
        _out.Write(writeQueue.ToString());
        _out.Flush();
    }

    private static string FormatTime(TimeSpan timeSpan)
    {
        return timeSpan >= TimeSpan.FromHours(1) ? timeSpan.ToString(@"hh\:mm\:ss") : timeSpan.ToString(@"mm\:ss");
    }

    private void WriteLine(StringBuilder queue, string text)
    {
        _displayLineCount++;
        queue.Append(text);
        queue.AppendLine();
    }

    private void WriteKvp(StringBuilder queue, string key, string value)
    {
        _displayLineCount++;
        queue.Append(key);
        queue.Append(": ");
        queue.Append(value);
        queue.AppendLine();
    }

    public IDebugWriter GetDebugWriter() => _debugWriter;

    private void EnableStartOnce()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 1)
        {
            throw new InvalidOperationException("Cannot start display more than once");
        }
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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

    private class StringQueueDebugWriter : IDebugWriter
    {
        private readonly ConcurrentQueue<string> _queue = new();
        private CancellationTokenSource _currentCancellationTokenSource = new();

        public CancellationTokenSource GetNextCancellationTokenSource(CancellationToken linkedCancellation = default)
        {
            _currentCancellationTokenSource.Dispose();
            _currentCancellationTokenSource = linkedCancellation == CancellationToken.None
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(linkedCancellation);
            return _currentCancellationTokenSource;
        }

        public bool TryDequeue([NotNullWhen(true)] out string? value)
        {
            return _queue.TryDequeue(out value);
        }

        public void Write(string text)
        {
            _queue.Enqueue(text);
            _currentCancellationTokenSource.Cancel();
        }

        public void WriteLine(string text)
        {
            _queue.Enqueue($"{text}{Environment.NewLine}");
            _currentCancellationTokenSource.Cancel();
        }
    }
}
