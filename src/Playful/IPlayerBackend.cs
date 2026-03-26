namespace Playful;

public interface IPlayerBackend : IDisposable
{
    public int GetQueuedSamples();

    public int GetSampleOffset();

    public PlayState GetPlayState();

    public void Play(bool restart);

    public void Stop();

    public void QueueBuffer<TSample>(ReadOnlyMemory<TSample> buffer) where TSample : unmanaged;

    public Task WaitForNextLoopAsync(Action iterationAction, CancellationToken cancellationToken = default);

    public Task WaitForFinishAsync(Action iterationAction, CancellationToken cancellationToken = default);
}
