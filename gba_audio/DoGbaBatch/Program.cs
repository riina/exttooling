using DoGbaBatch;
using MeltySynth;

const int sampleRate = 22050;

AR.Require("in-dir").Require("out-dir").KeyDo((r, _) => {
    string inDir = Path.GetFullPath(r["in-dir"]), outDir = Path.GetFullPath(r["out-dir"]);
    if (!Directory.Exists(inDir)) AR.Exit($"{inDir}: does not exist", 1);
    if (File.Exists(outDir)) AR.Exit($"{outDir}: is a file", 2);
    AR.Exit(() => Directory.CreateDirectory(outDir), $"{outDir}: failed to create output directory", 3);
    var files = Directory.GetFiles(inDir);
    var soundfonts = files.Where(v => Path.GetExtension(v.ToLowerInvariant()) == ".sf2").ToList();
    if (!soundfonts.Any()) AR.Exit($"{inDir}: no soundfont file in folder");
    if (soundfonts.Count != 1) AR.Exit($"{inDir}: multiple soundfonts detected in");
    var soundfont = soundfonts.First();
    var midis = files.Where(v => Path.GetExtension(v.ToLowerInvariant()) == ".mid").ToList();
    if (!midis.Any()) return;
    var synthesizer = new Synthesizer(soundfont, sampleRate);
    foreach (var midi in midis) {
        var midiFile = new MidiFile(midi);
        var sequencer = new MidiFileSequencer(synthesizer);
        sequencer.Play(midiFile, false);
        int numSamples = (int)(sampleRate * midiFile.Length.TotalSeconds);
        var left = new float[numSamples];
        var right = new float[numSamples];
        sequencer.Render(left, right);
        string from = midi, to = Path.Combine(outDir, Path.ChangeExtension(from, ".wav"));
        Console.Write($"{from} -> {to}...");
        using var fs = File.OpenWrite(to);
        Pcm.WritePcmWave(fs, PcmInfo.CreateFloat<float>(2, sampleRate, numSamples), left, right);
        Console.WriteLine("Done");
    }
});