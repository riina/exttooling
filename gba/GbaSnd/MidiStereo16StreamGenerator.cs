using MeltySynth;

namespace GbaSnd;

public class MidiStereo16StreamGenerator : Stereo16StreamGenerator
{
    private readonly MidiFileSequencer _sequencer;
    private readonly MidiFile _midi;
    private readonly int _numSamples;
    private int _sample;

    public override int Frequency { get; }

    public MidiStereo16StreamGenerator(MidiFileSequencer sequencer, MidiFile midi, int sampleRate, double duration)
    {
        _sequencer = sequencer;
        _midi = midi;
        Frequency = sampleRate;
        _numSamples = (int)(sampleRate * duration);
        _sample = 0;
    }

    public override void Reset()
    {
        _sequencer.Play(_midi, false);
        _sample = 0;
    }

    public override async ValueTask<int> FillBufferAsync(Memory<short> buffer, CancellationToken cancellationToken = default) =>
        await Task.Run(() =>
        {
            int numSamples = Math.Min(buffer.Length / 2, _numSamples - _sample);
            if (numSamples == 0) return numSamples;
            _sequencer.RenderInterleavedInt16(buffer.Span[..(numSamples * 2)]);
            _sample += numSamples;
            return numSamples;
        }, cancellationToken);
}
