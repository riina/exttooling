/*
Extracted some code from:
https://github.com/Lucina/Fp/blob/main/src/Fp.Plus/Audio/PcmData.cs
@83bb6a7

Modifications:
-WritePcmWave uses Stream.Write as opposed to Processor.Write
-s_chunkNames => ChunkNames
-Public WritePcmWave
-Internal ChunkNames
-Byte span optimization on s_chunkNames
-Interleaving float overload
-Added fact chunk header
-Added support for fact chunk
 */

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using DoGbaBatch;

static class Pcm {
    internal const int IndexChunkRiff = 0;
    internal const int IndexChunkWave = 4;
    internal const int IndexChunkFmt = 8;
    internal const int IndexChunkData = 12;
    internal const int IndexChunkFact = 16;

    internal static ReadOnlySpan<byte> ChunkNames => new byte[] {
        // "RIFF"
        0x52, 0x49, 0x46, 0x46,
        // "WAVE"
        0x57, 0x41, 0x56, 0x45,
        // "fmt "
        0x66, 0x6d, 0x74, 0x20,
        // "data"
        0x64, 0x61, 0x74, 0x61,
        // "fact"
        0x66, 0x61, 0x63, 0x74
    };

    public static void WritePcmWave(Stream outputStream, PcmInfo pcmInfo, ReadOnlySpan<float> left, ReadOnlySpan<float> right) {
        if (left.Length != right.Length) throw new ArgumentException("Left and right buffers should have same length");
        float[] iBuf = ArrayPool<float>.Shared.Rent(left.Length * 2);
        try {
            for (int i = 0; i < left.Length; i++) {
                iBuf[i * 2] = left[i];
                iBuf[i * 2 + 1] = right[i];
            }
            WritePcmWave(outputStream, pcmInfo, MemoryMarshal.Cast<float, byte>(iBuf));
        }
        finally {
            ArrayPool<float>.Shared.Return(iBuf);
        }
    }


    // http://soundfile.sapp.org/doc/WaveFormat/
    public static void WritePcmWave(Stream outputStream, PcmInfo pcmInfo, ReadOnlySpan<byte> data) {
        if (pcmInfo.SubChunk1Size is > 0x10 and < 0x12) throw new ArgumentException($"Unexpected {nameof(PcmInfo.SubChunk1Size)}");
        int factSize = 8 + (pcmInfo.Fact?.Length ?? -8);
        int hLen = 12 + 8 + pcmInfo.SubChunk1Size + factSize + 8;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(hLen);
        Span<byte> bufferSpan = buffer.AsSpan(0, hLen);
        try {
            // RIFF (main chunk)
            ChunkNames.Slice(IndexChunkRiff, 4).CopyTo(bufferSpan.Slice(0));
            BinaryPrimitives.WriteInt32LittleEndian(bufferSpan.Slice(4), 4 + 8 + pcmInfo.SubChunk1Size + factSize + 8 + pcmInfo.SubChunk2Size);
            // WAVE chunks
            ChunkNames.Slice(IndexChunkWave, 4).CopyTo(bufferSpan.Slice(8));
            // fmt (subchunk1)
            ChunkNames.Slice(IndexChunkFmt, 4).CopyTo(bufferSpan.Slice(0xC));
            BinaryPrimitives.WriteInt32LittleEndian(bufferSpan.Slice(0x10), pcmInfo.SubChunk1Size);
            BinaryPrimitives.WriteInt16LittleEndian(bufferSpan.Slice(0x14), pcmInfo.AudioFormat);
            BinaryPrimitives.WriteInt16LittleEndian(bufferSpan.Slice(0x16), pcmInfo.NumChannels);
            BinaryPrimitives.WriteInt32LittleEndian(bufferSpan.Slice(0x18), pcmInfo.SampleRate);
            BinaryPrimitives.WriteInt32LittleEndian(bufferSpan.Slice(0x1C), pcmInfo.ByteRate);
            BinaryPrimitives.WriteInt16LittleEndian(bufferSpan.Slice(0x20), pcmInfo.BlockAlign);
            BinaryPrimitives.WriteInt16LittleEndian(bufferSpan.Slice(0x22), pcmInfo.BitsPerSample);
            if (pcmInfo.SubChunk1Size >= 0x12) {
                BinaryPrimitives.WriteInt16LittleEndian(bufferSpan.Slice(0x24), pcmInfo.ExtraParamSize);
                pcmInfo.ExtraParams?.Span.Slice(0, pcmInfo.ExtraParamSize).CopyTo(bufferSpan.Slice(0x26));
            }
            // fact (if applicable)
            if (pcmInfo.Fact is { } fact) {
                ChunkNames.Slice(IndexChunkFact, 4).CopyTo(bufferSpan.Slice(12 + 8 + pcmInfo.SubChunk1Size));
                BinaryPrimitives.WriteInt32LittleEndian(bufferSpan.Slice(12 + 8 + pcmInfo.SubChunk1Size + 4), fact.Length);
                fact.Span.CopyTo(bufferSpan.Slice(12 + 8 + pcmInfo.SubChunk1Size + 8));
            }
            // data (subchunk2)
            int dataPos = 12 + 8 + pcmInfo.SubChunk1Size;
            ChunkNames.Slice(IndexChunkData, 4).CopyTo(bufferSpan.Slice(dataPos));
            BinaryPrimitives.WriteInt32LittleEndian(bufferSpan.Slice(dataPos + 4), pcmInfo.SubChunk2Size);

            outputStream.Write(buffer, 0, hLen);
        }
        finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        outputStream.Write(data);
    }
}