using System.CommandLine;
using Fp.Plus.Audio;
using GbaMus;
using MeltySynth;

var rootCommand = new Gba2WavRootCommand("Create WAV files from GBA file or directory with converted outputs");
var parseResult = rootCommand.Parse(args);
parseResult.InvocationConfiguration.Output = Console.Error;
parseResult.InvocationConfiguration.Error = Console.Error;
return await parseResult.InvokeAsync();

class Gba2WavRootCommand : RootCommand
{
    private const int SampleRate = 22050;
    private static readonly SynthesizerSettings s_synthesizerSettings = new(SampleRate);
    private readonly Argument<FileSystemInfo> _gbaFileOrInputDirectoryArgument;
    private readonly Argument<DirectoryInfo> _outputDirectoryArgument;
    private readonly Option<bool> _echoOption;

    public Gba2WavRootCommand(string description) : base(description)
    {
        SetAction(RunAsync);
        _gbaFileOrInputDirectoryArgument = new Argument<FileSystemInfo>("gba-file-or-input-directory")
        {
            Description = "The input GBA file or input directory", //
            Arity = ArgumentArity.ExactlyOne //
        }.AcceptExistingOnly();
        Add(_gbaFileOrInputDirectoryArgument);
        _outputDirectoryArgument = new Argument<DirectoryInfo>("output-directory")
        {
            Description = "Output directory", //
            Arity = ArgumentArity.ExactlyOne //
        }.AcceptLegalFilePathsOnly();
        Add(_outputDirectoryArgument);
        _echoOption = new Option<bool>("--echo") { HelpName = "enable-echo", Description = "Enable echo" };
        Add(_echoOption);
    }

    private async Task<int> RunAsync(ParseResult parseResult)
    {
        FileSystemInfo gbaFileOrInputDirectory = parseResult.GetRequiredValue(_gbaFileOrInputDirectoryArgument);
        DirectoryInfo outputDirectory = parseResult.GetRequiredValue(_outputDirectoryArgument);
        bool echo = parseResult.GetValue(_echoOption);
        s_synthesizerSettings.EnableReverbAndChorus = echo;
        if (File.Exists(outputDirectory.FullName))
        {
            WriteErrorLine($"{outputDirectory.FullName}: is a file");
            return 2;
        }
        try
        {
            outputDirectory.Create();
        }
        catch
        {
            WriteErrorLine($"{outputDirectory.FullName}: failed to create output directory");
            return 3;
        }
        if (gbaFileOrInputDirectory is FileInfo { Exists: true } gbaFile)
        {
            await using FileStream ms = gbaFile.OpenRead();
            MemoryRipper mr = new(ms, new GbaMusRipper.Settings(ImproveSoundfontCompliance: true));
            MemoryStream soundfontStream = new();
            mr.WriteSoundFont(soundfontStream);
            soundfontStream.Position = 0;
            var synthesizer = new Synthesizer(new SoundFont(soundfontStream), s_synthesizerSettings);
            foreach (int song in mr.Songs)
            {
                MemoryStream songStream = new();
                mr.WriteMidi(songStream, song);
                songStream.Position = 0;
                FileInfo from = new(Path.Join(outputDirectory.FullName, $"song{song:D4}.mid"));
                await using (var fs = from.Create())
                {
                    await songStream.CopyToAsync(fs);
                }
                songStream.Position = 0;
                FileInfo to = new(Path.Join(outputDirectory.FullName, $"song{song:D4}.wav"));
                Console.Write($"{to.FullName}... ");
                MidiFile midiFile = new(songStream);
                Render(synthesizer, midiFile, SampleRate, out int numSamples, out float[] left, out float[] right);
                await using (var fs = to.Create())
                {
                    Wave.WriteStereoFloatWave(fs, SampleRate, numSamples, left, right);
                }
                Console.WriteLine("Done");
                songStream.SetLength(0);
            }
        }
        else if (gbaFileOrInputDirectory is DirectoryInfo { Exists: true } inputDirectory)
        {
            FileInfo[] files = inputDirectory.GetFiles();
            var soundfonts = files.Where(v => v.Extension.Equals(".sf2", StringComparison.InvariantCultureIgnoreCase)).ToList();
            if (!soundfonts.Any())
            {
                WriteErrorLine($"{inputDirectory.FullName}: no soundfont file in folder");
                return 1;
            }
            if (soundfonts.Count != 1)
            {
                WriteErrorLine($"{inputDirectory.FullName}: multiple soundfonts detected in");
                return 1;
            }
            FileInfo soundfont = soundfonts.First();
            var midis = files.Where(v => v.Extension.Equals(".mid", StringComparison.InvariantCultureIgnoreCase)).ToList();
            if (!midis.Any()) return 0;
            var synthesizer = new Synthesizer(soundfont.FullName, s_synthesizerSettings);
            foreach (FileInfo midi in midis)
            {
                MidiFile midiFile;
                using (var ifs = midi.OpenRead())
                {
                    midiFile = new MidiFile(ifs);
                }
                Console.Write($"{midi.Name}... ");
                Render(synthesizer, midiFile, SampleRate, out int numSamples, out float[] left, out float[] right);
                FileInfo to = new(Path.Join(outputDirectory.FullName, Path.ChangeExtension(midi.Name, ".wav")));
                Console.Write($"-> {to}... ");
                await using (var fs = to.Create())
                {
                    Wave.WriteStereoFloatWave(fs, SampleRate, numSamples, left, right);
                }
                Console.WriteLine("Done");
            }
        }
        else
        {
            WriteErrorLine($"{gbaFileOrInputDirectory.FullName}: does not exist");
            return 1;
        }
        return 0;
    }

    private static void WriteErrorLine(string value)
    {
        Console.Error.WriteLine(value);
    }

    private static void Render(Synthesizer synthesizer, MidiFile midiFile, int sampleRate, out int numSamples, out float[] left, out float[] right)
    {
        var sequencer = new MidiFileSequencer(synthesizer);
        sequencer.Play(midiFile, false);
        numSamples = (int)(sampleRate * midiFile.Length.TotalSeconds);
        left = new float[numSamples];
        right = new float[numSamples];
        sequencer.Render(left, right);
    }
}
