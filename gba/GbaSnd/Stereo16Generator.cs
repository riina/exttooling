namespace GbaSnd;

public abstract class Stereo16Generator : IDisposable
{
    public abstract int Frequency { get; }
    public abstract int Length { get; }
    public abstract void Reset(int sample);
    public abstract ValueTask<int> FillBufferAsync(int samples, Memory<short> buffer, CancellationToken cancellationToken = default);

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
