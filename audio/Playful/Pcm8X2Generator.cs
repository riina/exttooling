namespace Playful;

public abstract class Pcm8X2Generator : SoundGenerator<sbyte>
{
    public override AudioFormat Format => AudioFormat.Pcm8X2;
}
