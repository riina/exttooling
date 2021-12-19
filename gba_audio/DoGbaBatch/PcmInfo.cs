/*
Retrieved from:
https://github.com/Lucina/Fp/blob/main/src/Fp.Plus/Audio/PcmInfo.cs
@83bb6a7

Modifications:
-Namespace change
-Replace CloneBuffer with ToArray (makes more sense anyway)
-Removed constructor overload for int format
-Removed constructor overload for ROM ExtraParams
-Added explicit static creation methods for int / float formats
-Added internal buffer factory for constructing "fact" chunk
 */

using System.Buffers.Binary;

namespace DoGbaBatch;

/// <summary>
/// PCM metadata.
/// </summary>
public record PcmInfo {
    /// <summary>
    /// 16 for PCM.  This is the size of the
    /// rest of the Subchunk which follows this number.
    /// </summary>
    public int SubChunk1Size { get; init; }

    /// <summary>
    /// PCM = 1 (i.e. Linear quantization)
    /// Values other than 1 indicate some
    /// form of compression.
    /// </summary>
    public short AudioFormat { get; init; }

    /// <summary>
    /// Mono = 1, Stereo = 2, etc.
    /// </summary>
    public short NumChannels { get; init; }

    /// <summary>
    /// Number of samples.
    /// </summary>
    public int NumSamples { get; init; }

    /// <summary>
    /// 8000, 44100, etc.
    /// </summary>
    public int SampleRate { get; init; }

    /// <summary>
    /// == SampleRate * NumChannels * BitsPerSample/8.
    /// </summary>
    public int ByteRate { get; init; }

    /// <summary>
    /// == NumChannels * BitsPerSample/8
    /// The number of bytes for one sample including
    /// all channels.
    /// </summary>
    public short BlockAlign { get; init; }

    /// <summary>
    /// 8 bits = 8, 16 bits = 16, etc.
    /// </summary>
    public short BitsPerSample { get; init; }

    /// <summary>
    /// if PCM, then doesn't exist.
    /// </summary>
    public short ExtraParamSize { get; init; }

    /// <summary>
    /// space for extra parameters.
    /// </summary>
    public ReadOnlyMemory<byte>? ExtraParams { get; init; }

    /// <summary>
    /// == NumSamples * NumChannels * BitsPerSample/8
    /// This is the number of bytes in the data.
    /// You can also think of this as the size
    /// of the read of the subchunk following this
    /// number.
    /// </summary>
    public int SubChunk2Size { get; init; }

    /// <summary>
    /// "fact" chunk (only necessary for non-PCM).
    /// </summary>
    public ReadOnlyMemory<byte>? Fact { get; init; }

    /// <summary>
    /// Creates a new instance of <see cref="PcmInfo"/>.
    /// </summary>
    /// <param name="subChunk1Size">16 for PCM.  This is the size of the
    /// rest of the Subchunk which follows this number.</param>
    /// <param name="audioFormat">PCM = 1 (i.e. Linear quantization)
    /// Values other than 1 indicate some
    /// form of compression.</param>
    /// <param name="numChannels">Mono = 1, Stereo = 2, etc.</param>
    /// <param name="sampleRate">8000, 44100, etc.</param>
    /// <param name="byteRate">== SampleRate * NumChannels * BitsPerSample/8.</param>
    /// <param name="blockAlign">== NumChannels * BitsPerSample/8
    /// The number of bytes for one sample including
    /// all channels.</param>
    /// <param name="bitsPerSample">8 bits = 8, 16 bits = 16, etc.</param>
    /// <param name="extraParamSize">if PCM, then doesn't exist.</param>
    /// <param name="extraParams">space for extra parameters.</param>
    /// <param name="subChunk2Size">== NumSamples * NumChannels * BitsPerSample/8
    /// This is the number of bytes in the data.
    /// You can also think of this as the size
    /// of the read of the subchunk following this
    /// number.</param>
    public PcmInfo(int subChunk1Size, short audioFormat, short numChannels, int sampleRate, int byteRate,
        short blockAlign, short bitsPerSample, short extraParamSize, ReadOnlyMemory<byte>? extraParams,
        int subChunk2Size, ReadOnlyMemory<byte>? fact) {
        SubChunk1Size = subChunk1Size;
        AudioFormat = audioFormat;
        NumChannels = numChannels;
        NumSamples = subChunk2Size * 8 / numChannels / bitsPerSample;
        SampleRate = sampleRate;
        ByteRate = byteRate;
        BlockAlign = blockAlign;
        BitsPerSample = bitsPerSample;
        ExtraParamSize = extraParamSize;
        ExtraParams = extraParams;
        SubChunk2Size = subChunk2Size;
        Fact = fact;
    }

    public static byte[] CreateFactChunk(int numSamples, int contentSize = 4) {
        if (contentSize < 4) throw new ArgumentException("Content size must be at least 4");
        byte[] buf = new byte[contentSize];
        BinaryPrimitives.WriteInt32LittleEndian(buf, numSamples);
        return buf;
    }

    /// <summary>
    /// Creates a new instance of <see cref="PcmInfo"/> for integer PCM data.
    /// </summary>
    /// <param name="numChannels">Mono = 1, Stereo = 2, etc.</param>
    /// <param name="sampleRate">8000, 44100, etc.</param>
    /// <param name="bitsPerSample">8 bits = 8, 16 bits = 16, etc.</param>
    /// <param name="numSamples">Number of samples (shared count between channels).</param>
    public static PcmInfo CreateInteger(short numChannels, int sampleRate, short bitsPerSample, int numSamples)
        => new(0x10, 1, numChannels, sampleRate, sampleRate * numChannels * bitsPerSample / 8,
            (short)(numChannels * bitsPerSample / 8), bitsPerSample, 0, null,
            numSamples * numChannels * bitsPerSample / 8, null);

    /// <summary>
    /// Creates a new instance of <see cref="PcmInfo"/> for integer PCM data.
    /// </summary>
    /// <param name="numChannels">Mono = 1, Stereo = 2, etc.</param>
    /// <param name="sampleRate">8000, 44100, etc.</param>
    /// <param name="numSamples">Number of samples (shared count between channels).</param>
    /// <typeparam name="T">Sample type.</typeparam>
    public static unsafe PcmInfo CreateInteger<T>(short numChannels, int sampleRate, int numSamples) where T : unmanaged {
        short bitsPerSample = (short)(sizeof(T) * 8);
        return new(0x10, 1, numChannels, sampleRate, sampleRate * numChannels * bitsPerSample / 8,
            (short)(numChannels * bitsPerSample / 8), bitsPerSample, 0, null,
            numSamples * numChannels * bitsPerSample / 8, null);
    }

    /// <summary>
    /// Creates a new instance of <see cref="PcmInfo"/> for float PCM data.
    /// </summary>
    /// <param name="numChannels">Mono = 1, Stereo = 2, etc.</param>
    /// <param name="sampleRate">8000, 44100, etc.</param>
    /// <param name="numSamples">Number of samples (shared count between channels).</param>
    /// <typeparam name="T">Sample type.</typeparam>
    public static unsafe PcmInfo CreateFloat<T>(short numChannels, int sampleRate, int numSamples) where T : unmanaged {
        short bitsPerSample = (short)(sizeof(T) * 8);
        return new PcmInfo(0x10, 3, numChannels, sampleRate, sampleRate * numChannels * bitsPerSample / 8,
            (short)(numChannels * bitsPerSample / 8), bitsPerSample, 0, null,
            numSamples * numChannels * bitsPerSample / 8, CreateFactChunk(numSamples));
    }
}