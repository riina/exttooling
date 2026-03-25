using MeltySynth;
using Playful.Common;
using Playful.Common.Generators;

namespace Playful.Gba;

public class MidiPcm16X2Generator : Pcm16X2Generator
{
    private const int NumChannels = 2;
    private const int CacheBufferSamples = 8 * 1024;
    //private const int MaxCacheBufferSeconds = 10;
    private const int MaxCacheBufferSecondsBackward = 30;
    private const int MaxCacheBufferSecondsForward = 15;
    private readonly MidiFileSequencer _sequencer;
    private readonly MidiFile _midi;
    private readonly int _numSamples;
    private int _nextSequencerSample;
    private int _nextPlaybackSample;
    private readonly SampleCache<short> _sampleCache;

    public override int Frequency { get; }
    public override int Length { get; }

    public MidiPcm16X2Generator(MidiFileSequencer sequencer, MidiFile midi, int sampleRate, double duration)
    {
        _sequencer = sequencer;
        _midi = midi;
        Frequency = sampleRate;
        _numSamples = (int)(sampleRate * duration);
        Length = _numSamples;
        _sampleCache = new SampleCache<short>(NumChannels);
        ResetPlayer();
    }

    private void ResetPlayer()
    {
        _sequencer.Play(_midi, false);
        _nextSequencerSample = 0;
    }

    public override void Reset(int sample)
    {
        _nextPlaybackSample = sample;
    }

    public override Range GetSurroundingCachedSampleRange(int sampleIndex)
    {
        return _sampleCache.GetSurroundingCachedSampleRange(sampleIndex);
    }

    public override int FillBuffer(int samples, Memory<short> buffer, CancellationToken cancellationToken = default)
    {
        return LoadBuffer(samples, buffer);
    }

    public override async ValueTask<int> FillBufferAsync(int samples, Memory<short> buffer, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => LoadBuffer(samples, buffer), cancellationToken);
    }

    private int LoadBuffer(int samples, Memory<short> buffer)
    {
        if (samples <= 0)
        {
            return 0;
        }
        int numSamples;
        if (_sampleCache.TryGetCacheBuffer(_nextPlaybackSample, out int eSamples, out Memory<short> eBuffer))
        {
            numSamples = Math.Min(samples, eSamples);
        }
        else
        {
            if (_nextSequencerSample > _nextPlaybackSample)
            {
                ResetPlayer();
            }
            int firstSequencerSample;
            do
            {
                CreateNextBuffer(out firstSequencerSample, out eBuffer, out numSamples);
            } while (firstSequencerSample + numSamples <= _nextPlaybackSample && numSamples > 0);
            if (numSamples <= 0)
            {
                return 0;
            }
            int trimStart = _nextPlaybackSample - firstSequencerSample;
            numSamples -= trimStart;
            numSamples = Math.Min(samples, numSamples);
            eBuffer = eBuffer.Slice(trimStart * NumChannels, numSamples * NumChannels);
        }
        if (numSamples <= 0)
        {
            return 0;
        }
        eBuffer.Span[..(numSamples * NumChannels)].CopyTo(buffer.Span);
        _nextPlaybackSample += numSamples;
        DiscardExcess(_nextPlaybackSample);
        return numSamples;
    }

    public override string GetDebugText()
    {
        return $"seq:{_nextSequencerSample:X06} play:{_nextPlaybackSample:X06} {_nextSequencerSample - _nextPlaybackSample:X06}";
    }

    private void CreateNextBuffer(out int firstSequencerSample, out Memory<short> eBuffer, out int numSamples)
    {
        firstSequencerSample = _nextSequencerSample;
        eBuffer = new short[CacheBufferSamples * NumChannels];
        numSamples = ReadAndCache(CacheBufferSamples, eBuffer);
    }

    private int ReadAndCache(int samples, Memory<short> buffer)
    {
        int available = _numSamples - _nextSequencerSample;
        if (available <= 0)
        {
            return 0;
        }
        int numSamples = Math.Min(samples, available);
        var subMemory = buffer[..(numSamples * NumChannels)];
        _sequencer.RenderInterleavedInt16(subMemory.Span);
        _sampleCache.TryAdd(new Range(_nextSequencerSample, _nextSequencerSample + numSamples), subMemory);
        _nextSequencerSample += numSamples;
        return numSamples;
    }

    private void DiscardExcess(int sampleAxis)
    {
        int minSample = (int)Math.Max(0, sampleAxis - (long)MaxCacheBufferSecondsBackward * Frequency);
        int maxSample = (int)Math.Min(int.MaxValue, sampleAxis + (long)MaxCacheBufferSecondsForward * Frequency);
        _sampleCache.DiscardExcess(minSample, maxSample);
        //int maxSampleCacheSamples = (int)Math.Floor(MaxCacheBufferSeconds * (double)Frequency / CacheBufferSamples);
        //_sampleCache.DiscardExcessLegacy(maxSampleCacheSamples, _iSample);
    }
}
