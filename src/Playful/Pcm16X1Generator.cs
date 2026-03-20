namespace Playful;

public abstract class Pcm16X1Generator : SoundGenerator<short>
{
    public override AudioFormat Format => AudioFormat.Pcm16X1;
}
