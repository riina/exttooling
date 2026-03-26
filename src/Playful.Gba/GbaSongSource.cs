using System.Text;
using GbaMus;
using MeltySynth;
using Playful.Midi;

namespace Playful.Gba;

public class GbaSongSource
{
    private static readonly Dictionary<string, string> s_codeMap = new() { { "01", "Nintendo" }, { "08", "Capcom" } };
    public static readonly IReadOnlyDictionary<string, string> CodeMap = s_codeMap;
    public readonly IReadOnlyList<GbaSong> Songs;

    private const int SampleRate = 48000; //22050;
    private readonly MemoryRipper _mr;
    private readonly MidiFileSequencer _sequencer;

    public GbaSongSource(Stream stream, GbaMusRipper.Settings? settings = null, int trackThreshold = 3)
    {
        MemoryStream ms = new();
        stream.CopyTo(ms);
        Span<byte> tmp = stackalloc byte[12];
        tmp.Clear();
        ms.Position = 0xA0;
        string gameCode = ReadUtf8String(ms, tmp[..12], out _, out _);
        ms.Position = 0xB0;
        string makerCode = ReadUtf8String(ms, tmp[..2], out _, out _);
        s_codeMap.TryGetValue(makerCode, out string? maker);
        _mr = new MemoryRipper(ms, settings ?? new GbaMusRipper.Settings(ImproveSoundfontCompliance: true));
        MemoryStream soundfontStream = new();
        _mr.WriteSoundFont(soundfontStream);
        soundfontStream.Position = 0;
        var synthesizerSettings = new SynthesizerSettings(SampleRate) { EnableReverbAndChorus = false };
        Synthesizer synthesizer = new(new SoundFont(soundfontStream), synthesizerSettings);
        _sequencer = new MidiFileSequencer(synthesizer);
        List<GbaSong> songs = new();
        int i = 0;
        foreach (int song in _mr.Songs)
        {
            try
            {
                SongRipper sr = _mr.GetSongRipper(song, true);
                int trackCount = sr.TrackCount;
                if (trackCount < trackThreshold)
                {
                    continue;
                }
                songs.Add(new GbaSong(this, song, gameCode, i++, maker, sr.CalculateDuration() is var d ? TimeSpan.FromSeconds(d) : null));
            }
            catch
            {
                // ignored
            }
        }
        Songs = songs;
    }

    internal MidiPcm16X2Generator GetGenerator(int songId)
    {
        MemoryStream songStream = new();
        _mr.WriteMidi(songStream, songId);
        songStream.Position = 0;
        MidiFile midiFile = new(songStream);
        return new MidiPcm16X2Generator(_sequencer, midiFile, SampleRate, midiFile.Length.TotalSeconds);
    }

    public static string ReadUtf8String(Stream stream, Span<byte> tmpBuffer, out int read, out int numBytes)
    {
        read = 0;
        numBytes = 0;
        do
        {
            int v = stream.ReadByte();
            read += v == -1 ? 0 : 1;
            if (v is -1 or 0) break;
            tmpBuffer[numBytes++] = (byte)v;
        } while (read < tmpBuffer.Length);

        if (numBytes == 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(tmpBuffer[..numBytes]);
    }
}
