using Fp;
using GbaMus;
using MeltySynth;

namespace GbaSnd;

public class GbaSongLoader
{
    private static readonly Dictionary<string, string> _codeMap = new() { { "01", "Nintendo" }, { "08", "Capcom" } };
    public static readonly IReadOnlyDictionary<string, string> CodeMap = _codeMap;
    public readonly IReadOnlyList<GbaSong> Songs;

    private const int SampleRate = 22050;
    private readonly MemoryRipper _mr;
    private readonly MidiFileSequencer _sequencer;

    public GbaSongLoader(Stream stream, GbaMusRipper.Settings? settings = null, int trackThreshold = 3)
    {
        MemoryStream ms = new();
        stream.CopyTo(ms);
        string gameCode = Processor.Instance.ReadUtf8StringFromOffset(ms, 0xA0, out _, out _, 12);
        string makerCode = Processor.Instance.ReadUtf8StringFromOffset(ms, 0xB0, out _, out _, 2);
        _codeMap.TryGetValue(makerCode, out string? maker);
        _mr = new MemoryRipper(ms, settings ?? new GbaMusRipper.Settings());
        MemoryStream soundfontStream = new();
        _mr.WriteSoundFont(soundfontStream);
        soundfontStream.Position = 0;
        Synthesizer synthesizer = new(new SoundFont(soundfontStream), SampleRate);
        _sequencer = new MidiFileSequencer(synthesizer);
        List<GbaSong> songs = new();
        int i = 0;
        foreach (int song in _mr.Songs)
        {
            try
            {
                int trackCount = _mr.GetTrackCount(song);
                if (trackCount < trackThreshold) continue;
                songs.Add(new GbaSong(this, song, gameCode, i++, maker));
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
}
