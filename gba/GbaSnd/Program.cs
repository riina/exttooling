using GbaMus;
using GbaSnd;
using MeltySynth;

const int sampleRate = 22050;

await AR.Require("gba").Require("id").KeyDoAsync(async (r, _) =>
{
    string gba = Path.GetFullPath(r["gba"]), id = r["id"];
    if (!File.Exists(gba)) AR.Exit($"{gba}: does not exist", 2);
    if (!int.TryParse(id, out int songId)) AR.Exit("invalid id format");
    MemoryRipper mr;
    using (FileStream ms = File.OpenRead(gba)) mr = new MemoryRipper(ms, new GbaMusRipper.Settings());
    MemoryStream soundfontStream = new();
    mr.WriteSoundFont(soundfontStream);
    soundfontStream.Position = 0;
    var synthesizer = new Synthesizer(new SoundFont(soundfontStream), sampleRate);
    if (!mr.Songs.Contains(songId)) AR.Exit("Invalid song");
    MemoryStream songStream = new();
    mr.WriteMidi(songStream, songId);
    songStream.Position = 0;
    MidiFile midiFile = new(songStream);
    using MCtx c = new();
    var sg = new MidiStereo16StreamGenerator(new MidiFileSequencer(synthesizer), midiFile, sampleRate, midiFile.Length.TotalSeconds);
    using PCtx p = c.Stream(sg);
    Console.WriteLine($"{p.Duration}");
    await p.PlayAsync();
    Task prevTask = Task.CompletedTask;
    while (true)
    {
        await prevTask;
        if (p.PlayState == PlayState.Ended) break;
        int transport = 0;
        bool playing = p.PlayState == PlayState.Playing;
        bool setPlaying = playing;
        bool spaceLast = false;
        while (Console.KeyAvailable)
        {
            ConsoleKeyInfo cki = Console.ReadKey(true);
            switch (cki.Key)
            {
                case ConsoleKey.LeftArrow:
                    spaceLast = false;
                    transport -= 5;
                    break;
                case ConsoleKey.RightArrow:
                    spaceLast = false;
                    transport += 5;
                    break;
                case ConsoleKey.Spacebar:
                    spaceLast = true;
                    setPlaying ^= true;
                    break;
            }
        }
        if (!setPlaying && playing && spaceLast) p.Stop();
        if (transport != 0 || setPlaying && !playing) prevTask = p.PlaySeekAsync(transport);
        else prevTask = Task.CompletedTask;
    }
});
