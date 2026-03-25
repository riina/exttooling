using OpenTK.Audio.OpenAL;

namespace Playful.Common.Player;

public sealed class MPlayerContext : IDisposable
{
    private ALDevice _dev;
    private ALContext _context;
    private bool _disposed;

    public MPlayerContext()
    {
        _dev = ALC.OpenDevice(null);
        _context = ALC.CreateContext(_dev, new ALContextAttributes());
#pragma warning disable CS8073
        if (_dev == null)
        {
            throw new InvalidOperationException(AL.GetErrorString(AL.GetError()));
        }
#pragma warning restore CS8073
        try
        {
            if (!ALC.IsEnumerationExtensionPresent(_dev))
            {
                throw new NotSupportedException();
            }
            if (!ALC.MakeContextCurrent(_context))
            {
                throw new InvalidOperationException(AL.GetErrorString(AL.GetError()));
            }
        }
        catch
        {
            ALC.CloseDevice(_dev);
            _dev = default;
            throw;
        }
    }

    private void EnsureState()
    {
        if (_context.Handle == IntPtr.Zero)
        {
            throw new ObjectDisposedException(nameof(MPlayerContext));
        }
    }

    public MPlayerOutput Stream(SoundGenerator soundGenerator, TextWriter? debug = null)
    {
        EnsureState();
        return new MPlayerOutput(soundGenerator, debug);
    }

    private void ReleaseUnmanagedResources()
    {
        if (!ALC.MakeContextCurrent(default))
        {
            throw new InvalidOperationException(AL.GetErrorString(AL.GetError()));
        }
        if (_context.Handle != IntPtr.Zero)
        {
            ALC.DestroyContext(_context);
        }
        _context = default;
        if (_dev.Handle != IntPtr.Zero)
        {
            ALC.CloseDevice(_dev);
        }
        _dev = default;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // issue when stopping
                Thread.Sleep(TimeSpan.FromSeconds(0.5));
            }
            ReleaseUnmanagedResources();
        }
        _disposed = true;
    }

    ~MPlayerContext() => Dispose(false);
}
