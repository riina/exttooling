namespace GbaSnd;

public abstract class Pcm16X2Generator : SoundGenerator<short>
{
    public override AudioFormat Format => AudioFormat.Pcm16X2;
}
