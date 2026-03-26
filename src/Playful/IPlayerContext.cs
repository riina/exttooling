namespace Playful;

public interface IPlayerContext : IDisposable
{
    public MPlayerBackendCreationDelegate CreateBackend { get; }
}
