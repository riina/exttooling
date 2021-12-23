using MeltySynth;

namespace GbaSnd;

public class MidiPcm16X2Generator : Pcm16X2Generator
{
    private const int CacheBufferSamples = 8 * 1024;
    private const int MaxCacheBufferSeconds = 10;
    private readonly MidiFileSequencer _sequencer;
    private readonly MidiFile _midi;
    private readonly int _numSamples;
    private int _iSample;
    private int _oSample;
    private SortedList<int, CacheBuffer> _cache;

    private readonly record struct CacheBuffer(Range Range, int Samples, Memory<short> SampleBuffer);

    public override int Frequency { get; }
    public override int Length { get; }

    public MidiPcm16X2Generator(MidiFileSequencer sequencer, MidiFile midi, int sampleRate, double duration)
    {
        _sequencer = sequencer;
        _midi = midi;
        Frequency = sampleRate;
        _numSamples = (int)(sampleRate * duration);
        Length = _numSamples;
        _cache = new SortedList<int, CacheBuffer>();
        ResetPlayer();
    }

    private void ResetPlayer()
    {
        _sequencer.Play(_midi, false);
        _iSample = 0;
    }

    public override void Reset(int sample)
    {
        _oSample = sample;
    }

    public override async ValueTask<int> FillBufferAsync(int samples, Memory<short> buffer, CancellationToken cancellationToken = default) =>
        await Task.Run(() => LoadBuffer(samples, buffer), cancellationToken);

    private int LoadBuffer(int samples, Memory<short> buffer)
    {
        if (samples <= 0) return 0;
        int numSamples;
        if (TryGetCacheBuffer(_oSample, out int eSamples, out Memory<short> eBuffer))
        {
            numSamples = Math.Min(samples, eSamples);
        }
        else
        {
            if (_iSample > _oSample) ResetPlayer();
            int iSample;
            do
            {
                iSample = _iSample;
                eBuffer = new short[CacheBufferSamples * 2];
                numSamples = ReadAndCache(CacheBufferSamples, eBuffer);
            } while (iSample + numSamples <= _oSample && numSamples > 0);
            if (numSamples <= 0) return 0;
            int trimStart = _oSample - iSample;
            numSamples -= trimStart;
            numSamples = Math.Min(samples, numSamples);
            eBuffer = eBuffer.Slice(trimStart * 2, numSamples * 2);
        }
        if (numSamples <= 0) return 0;
        eBuffer.Span[..(numSamples * 2)].CopyTo(buffer.Span);
        _oSample += numSamples;
        return numSamples;
    }

    private int ReadAndCache(int samples, Memory<short> buffer)
    {
        int available = _numSamples - _iSample;
        if (available <= 0) return 0;
        int numSamples = Math.Min(samples, available);
        _sequencer.RenderInterleavedInt16(buffer.Span[..(numSamples * 2)]);
        if (!_cache.ContainsKey(_iSample)) _cache.Add(_iSample, new CacheBuffer(new Range(_iSample, _iSample + numSamples), numSamples, buffer[..(numSamples * 2)]));
        _iSample += numSamples;
        while (_cache.Count * CacheBufferSamples / (double)Frequency > MaxCacheBufferSeconds)
        {
            if (Math.Abs(_cache.Keys[0] - _iSample) > Math.Abs(_cache.Keys[^1] - _iSample))
                _cache.RemoveAt(0);
            else
                _cache.RemoveAt(_cache.Count - 1);
        }
        return numSamples;
    }

    private bool TryGetCacheBuffer(int index, out int samples, out Memory<short> buffer)
    {
        int l = 0, u = _cache.Count - 1;
        CacheBuffer b;
        while (l <= u)
        {
            int m = l + (u - l) / 2;
            b = _cache.Values[m];
            switch (index - b.Range.Start.Value)
            {
                case 0:
                    samples = b.Samples;
                    buffer = b.SampleBuffer;
                    return true;
                case > 0:
                    l = m + 1;
                    break;
                default:
                    u = m - 1;
                    break;
            }
        }
        if (l == 0)
        {
            samples = 0;
            buffer = default;
            return false;
        }
        b = _cache.Values[l - 1];
        if (b.Range.End.Value <= index)
        {
            samples = 0;
            buffer = default;
            return false;
        }
        samples = b.Samples - (index - b.Range.Start.Value);
        buffer = b.SampleBuffer[((index - b.Range.Start.Value) * 2)..];
        return true;
    }
}
