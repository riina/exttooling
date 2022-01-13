using Fp.Plus.Audio;
using GbaMus;
using MeltySynth;

const int sampleRate = 22050;
var synthesizerSettings = new SynthesizerSettings(sampleRate);
AR.Require("gba-or-dir").Require("out-dir").Optional("echo-on").KeyDo((r, o) =>
{
    string input = Path.GetFullPath(r["gba-or-dir"]), outDir = Path.GetFullPath(r["out-dir"]);
    synthesizerSettings.EnableReverbAndChorus = new HashSet<string>{"echo-on","true","y","e","yes"}.Contains(o["echo-on"]?.ToLowerInvariant()??"");
    if (File.Exists(outDir)) AR.Exit($"{outDir}: is a file", 2);
    AR.Exit(() => Directory.CreateDirectory(outDir), $"{outDir}: failed to create output directory", 3);
    if (File.Exists(input))
    {
        MemoryRipper mr;
        using (FileStream ms = File.OpenRead(input)) mr = new MemoryRipper(ms, new GbaMusRipper.Settings());
        MemoryStream soundfontStream = new();
        mr.WriteSoundFont(soundfontStream);
        soundfontStream.Position = 0;
        var synthesizer = new Synthesizer(new SoundFont(soundfontStream), synthesizerSettings);
        foreach (int song in mr.Songs)
        {
            MemoryStream songStream = new();
            mr.WriteMidi(songStream, song);
            songStream.Position = 0;
            /*string from = Path.Combine(outDir, $"song{song:D4}.mid");
            using (var fs = File.Create(from))
                songStream.CopyTo(fs);
            songStream.Position = 0;*/
            string to = Path.Combine(outDir, $"song{song:D4}.wav");
            Console.Write($"{to}... ");
            MidiFile midiFile = new(songStream);
            Render(synthesizer, midiFile, sampleRate, out int numSamples, out float[] left, out float[] right);
            using (var fs = File.Create(to))
                Wave.WriteStereoFloatWave<float>(fs, sampleRate, numSamples, left, right);
            Console.WriteLine("Done");
            songStream.SetLength(0);
        }
    }
    else if (Directory.Exists(input))
    {
        string[] files = Directory.GetFiles(input);
        var soundfonts = files.Where(v => Path.GetExtension(v.ToLowerInvariant()) == ".sf2").ToList();
        if (!soundfonts.Any()) AR.Exit($"{input}: no soundfont file in folder");
        if (soundfonts.Count != 1) AR.Exit($"{input}: multiple soundfonts detected in");
        string soundfont = soundfonts.First();
        var midis = files.Where(v => Path.GetExtension(v.ToLowerInvariant()) == ".mid").ToList();
        if (!midis.Any()) return;
        var synthesizer = new Synthesizer(soundfont, synthesizerSettings);
        foreach (string midi in midis)
        {
            MidiFile midiFile;
            using (var ifs = File.OpenRead(midi))
                midiFile = new MidiFile(ifs);
            Console.Write($"{Path.GetFileName(midi)}... ");
            Render(synthesizer, midiFile, sampleRate, out int numSamples, out float[] left, out float[] right);
            string to = Path.Combine(outDir, Path.ChangeExtension(Path.GetFileName(midi), ".wav"));
            Console.Write($"-> {to}... ");
            using (var fs = File.Create(to))
                Wave.WriteStereoFloatWave<float>(fs, sampleRate, numSamples, left, right);
            Console.WriteLine("Done");
        }
    }
    else
        AR.Exit($"{input}: does not exist", 1);
});

static void Render(Synthesizer synthesizer, MidiFile midiFile, int sampleRate, out int numSamples, out float[] left, out float[] right)
{
    var sequencer = new MidiFileSequencer(synthesizer);
    sequencer.Play(midiFile, false);
    numSamples = (int)(sampleRate * midiFile.Length.TotalSeconds);
    left = new float[numSamples];
    right = new float[numSamples];
    sequencer.Render(left, right);
}
