namespace GbaSnd;

public abstract class SoundGenerator : IDisposable
{
    public abstract AudioFormat Format { get; }

    public abstract int Frequency { get; }

    public abstract int Length { get; }

    public abstract void Reset(int sample);

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

public abstract class SoundGenerator<TSample> : SoundGenerator where TSample : unmanaged
{
    public abstract ValueTask<int> FillBufferAsync(int samples, Memory<TSample> buffer, CancellationToken cancellationToken = default);
}
