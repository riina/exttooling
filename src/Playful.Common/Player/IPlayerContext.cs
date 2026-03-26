namespace Playful.Common.Player;

public interface IPlayerContext : IDisposable
{
    public MPlayerBackendCreationDelegate CreateBackend { get; }
}
