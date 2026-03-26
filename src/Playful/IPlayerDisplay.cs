namespace Playful;

public interface IPlayerDisplay : IDisposable
{
    public bool ShowDebug { get; set; }

    public bool ShowCacheInfo { get; set; }

    public Task ExecuteAsync(CancellationToken cancellationToken = default);

    public void SetDisplayState(MPlayerDisplayState displayState);
}
