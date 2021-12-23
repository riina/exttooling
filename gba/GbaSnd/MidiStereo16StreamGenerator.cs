using MeltySynth;

namespace GbaSnd;

public class MidiStereo16StreamGenerator : Stereo16StreamGenerator
{
    private readonly MidiFileSequencer _sequencer;
    private readonly MidiFile _midi;
    private readonly int _numSamples;
    private int _sample;
    private List<CacheBuffer> _cache;

    private readonly record struct CacheBuffer(Range Range, Memory<short> SampleBuffer);

    public override int Frequency { get; }
    public override int Length { get; }

    public MidiStereo16StreamGenerator(MidiFileSequencer sequencer, MidiFile midi, int sampleRate, double duration)
    {
        _sequencer = sequencer;
        _midi = midi;
        Frequency = sampleRate;
        _numSamples = (int)(sampleRate * duration);
        Length = _numSamples;
        _sample = 0;
        _cache = new List<CacheBuffer>();
        _sequencer.Play(_midi, false);
    }

    public override void Reset(int sample)
    {
        // Method should only ever execute when no buffer tasks are running
        if (_sample == sample) return;
        _sequencer.Play(_midi, false);
        // TODO position sample
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
