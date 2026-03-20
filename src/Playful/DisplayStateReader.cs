namespace Playful;

public class DisplayStateReader
{
    private readonly Stream _stream;
    private bool _end;

    public DisplayStateReader(Stream stream)
    {
        _stream = stream;
    }

    public MPlayerDisplayState? ReadState()
    {
        if (_end) return null;
        _end = !boolSerialization.Deserialize(_stream);
        if (_end) return null;
        return MPlayerDisplayStateSerialization.Deserialize(_stream);
    }
}
