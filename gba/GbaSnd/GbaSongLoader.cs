using GbaMus;
using MeltySynth;

namespace GbaSnd;

public class GbaSongLoader
{
    public readonly IReadOnlyList<GbaSong> Songs;

    private const int SampleRate = 22050;
    private readonly MemoryRipper _mr;
    private readonly MidiFileSequencer _sequencer;

    public GbaSongLoader(Stream stream, GbaMusRipper.Settings? settings = null, int trackThreshold = 3)
    {
        _mr = new MemoryRipper(stream, settings ?? new GbaMusRipper.Settings());
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
                songs.Add(new GbaSong(this, i++, song));
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
