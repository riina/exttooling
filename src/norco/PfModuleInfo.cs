using Artcore;

namespace norco;

public abstract class PfModuleInfo(ALCModule module) : IDisposable
{
    private bool _disposed;
    private ALCModule? _module = module;

    protected virtual void Dispose(bool disposing)
    {
        _disposed = true;
        if (disposing)
        {
            _module?.AssemblyLoadContext.Unload();
            _module = null;
        }
    }


    public ALCModule GetModule()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_module is not { } module)
        {
            throw new InvalidOperationException();
        }
        return module;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

public sealed class PfModuleInfo<T>(ALCModule module, List<T> components) : PfModuleInfo(module)
{
    private bool _disposed;
    private List<T>? _components = components;

    public List<T> GetComponents()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_components is not { } components)
        {
            throw new InvalidOperationException();
        }
        return components;
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
        if (disposing)
        {
            _components?.Clear();
            _components = null;
        }
    }
}
