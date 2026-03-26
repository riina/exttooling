using OpenTK.Audio.OpenAL;

namespace Playful.OpenTK;

public sealed class MPlayerOpenALContext : IPlayerContext
{
    private ALDevice _dev;
    private ALContext _context;
    private bool _disposed;

    public static MPlayerOpenALContext Create()
    {
        return new MPlayerOpenALContext();
    }

    public MPlayerOpenALContext()
    {
        _dev = ALC.OpenDevice(null);
        _context = ALC.CreateContext(_dev, new ALContextAttributes());
        if (_dev.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(AL.GetErrorString(AL.GetError()));
        }
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

    public MPlayerBackendCreationDelegate CreateBackend => MPlayerOpenALBackend.Create;

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

    ~MPlayerOpenALContext() => Dispose(false);
}
