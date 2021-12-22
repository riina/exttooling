namespace GbaSnd;

public abstract class Stereo16StreamGenerator
{
    public abstract int Frequency { get; }
    public abstract void Reset();
    public abstract ValueTask<int> FillBufferAsync(Memory<short> buffer, CancellationToken cancellationToken = default);
}
