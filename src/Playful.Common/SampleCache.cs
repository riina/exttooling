namespace Playful.Common;

public class SampleCache<T> where T : unmanaged
{
    private readonly int _channelCount;

    private readonly record struct CacheBuffer(Range Range, Memory<T> SampleBuffer);

    private readonly SortedList<int, CacheBuffer> _cache;

    public SampleCache(int channelCount)
    {
        _channelCount = channelCount;
        _cache = new SortedList<int, CacheBuffer>();
    }

    public bool TryGetCacheBuffer(int index, out int samples, out Memory<T> buffer)
    {
        int l = 0, u = _cache.Count - 1;
        CacheBuffer b;
        int rangeStart;
        while (l <= u)
        {
            int m = l + (u - l) / 2;
            b = _cache.Values[m];
            rangeStart = b.Range.Start.Value;
            switch (index - rangeStart)
            {
                case 0:
                    samples = b.Range.End.Value - rangeStart;
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
        rangeStart = b.Range.Start.Value;
        int rangeEnd = b.Range.End.Value;
        if (rangeEnd <= index)
        {
            samples = 0;
            buffer = default;
            return false;
        }
        samples = rangeEnd - index;
        buffer = b.SampleBuffer[((index - rangeStart) * _channelCount)..];
        return true;
    }

    private bool TryGetCacheBufferUncut(int index, out int resultStart, out int samples, out Memory<T> buffer)
    {
        int l = 0, u = _cache.Count - 1;
        CacheBuffer b;
        int rangeStart;
        while (l <= u)
        {
            int m = l + (u - l) / 2;
            b = _cache.Values[m];
            rangeStart = b.Range.Start.Value;
            switch (index - rangeStart)
            {
                case 0:
                    resultStart = rangeStart;
                    samples = b.Range.End.Value - rangeStart;
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
            resultStart = -1;
            samples = 0;
            buffer = default;
            return false;
        }
        b = _cache.Values[l - 1];
        rangeStart = b.Range.Start.Value;
        int rangeEnd = b.Range.End.Value;
        if (rangeEnd <= index)
        {
            resultStart = -1;
            samples = 0;
            buffer = default;
            return false;
        }
        resultStart = rangeStart;
        samples = rangeEnd - rangeStart;
        buffer = b.SampleBuffer;
        return true;
    }

    public Range GetSurroundingCachedSampleRange(int sampleIndex)
    {
        int minSample, maxSample, tmpSampleCount, tmpResultStart;
        if (!TryGetCacheBufferUncut(sampleIndex, out minSample, out tmpSampleCount, out _))
        {
            return new Range();
        }
        maxSample = minSample + tmpSampleCount;
        while (minSample > 0 && TryGetCacheBufferUncut(minSample - 1, out tmpResultStart, out tmpSampleCount, out _))
        {
            minSample = tmpResultStart;
        }
        while (maxSample < int.MaxValue && TryGetCacheBufferUncut(maxSample, out tmpResultStart, out tmpSampleCount, out _))
        {
            maxSample = tmpResultStart + tmpSampleCount;
        }
        return new Range(minSample, maxSample);
    }

    public bool TryAdd(Range range, Memory<T> buffer)
    {
        if (range.Start.IsFromEnd || range.End.IsFromEnd || range.Start.Value < 0 || range.End.Value < range.Start.Value)
        {
            throw new ArgumentException("Invalid range", nameof(range));
        }
        (int rangeOffset, int rangeLength) = range.GetOffsetAndLength(range.End.Value);
        if (rangeLength * _channelCount != buffer.Length)
        {
            throw new ArgumentException(
                $"Buffer of length {buffer.Length} does not have correct number of samples for range length {rangeLength} with channel count {_channelCount}",
                nameof(buffer)
            );
        }
        return _cache.TryAdd(rangeOffset, new CacheBuffer(range, buffer));
    }

    public void DiscardExcess(int minSample, int maxSample)
    {
        if (minSample >= 0)
        {
            int lookIndex = 0;
            while (_cache.Count > 0 && _cache.Keys[lookIndex] < minSample)
            {
                var cacheEntry = _cache.Values[lookIndex];
                var cacheEntryRange = cacheEntry.Range;
                if (cacheEntryRange.End.Value > minSample)
                {
                    lookIndex++;
                    continue;
                }
                _cache.RemoveAt(lookIndex);
            }
        }
        if (maxSample >= 0)
        {
            int lookIndex = _cache.Count - 1;
            while (_cache.Count > lookIndex && _cache.Keys[lookIndex] > maxSample - 1)
            {
                _cache.RemoveAt(lookIndex);
                lookIndex--;
            }
        }
    }

    public void DiscardExcessLegacy(int maxSampleCount, int axisSampleIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxSampleCount);
        while (_cache.Count > maxSampleCount)
        {
            if (Math.Abs(_cache.Keys[0] - axisSampleIndex) > Math.Abs(_cache.Keys[^1] - axisSampleIndex))
            {
                _cache.RemoveAt(0);
            }
            else
            {
                _cache.RemoveAt(_cache.Count - 1);
            }
        }
    }
}
