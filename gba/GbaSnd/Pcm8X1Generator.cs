namespace GbaSnd;

public abstract class Pcm8X1Generator : SoundGenerator<sbyte>
{
    public override AudioFormat Format => AudioFormat.Pcm8X2;
}
