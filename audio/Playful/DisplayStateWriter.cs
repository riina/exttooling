namespace Playful;

public class DisplayStateWriter : IDisposable
{
    private readonly Stream _stream;
    private bool _disposed;

    public DisplayStateWriter(Stream stream)
    {
        _stream = stream;
    }

    public void WriteState(MPlayerDisplayState state)
    {
        boolSerialization.Serialize(true, _stream);
        MPlayerDisplayStateSerialization.Serialize(state, _stream);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        boolSerialization.Serialize(false, _stream);
    }
}
