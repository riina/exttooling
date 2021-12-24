using Playful;
using Playful.Gba;

await AR.Require("gba").Optional("id").DoAsync(async l =>
{
    string gba = Path.GetFullPath(l[0]);
    if (!File.Exists(gba)) AR.Exit($"{gba}: does not exist", 2);
    List<int> indices = new();
    foreach (string s in l.Skip(1))
    {
        if (s is not { } indexS || !int.TryParse(indexS, out int songIdv))
        {
            AR.Exit($"{s}: invalid id format");
            return;
        }
        indices.Add(songIdv);
    }
    GbaSongSource gsl;
    await using (FileStream fs = File.OpenRead(gba)) gsl = new GbaSongSource(fs);
    if (!indices.Any())
    {
        foreach (var s in gsl.Songs) Console.WriteLine($"{s.Artist} - {s.Name}{(s.Duration is { } d ? $" ({d:mm\\:ss})" : "")}");
        return;
    }
    List<GbaSong> songs = new();
    foreach (int i in indices)
    {
        if (i < 0 || i >= gsl.Songs.Count)
        {
            AR.Exit("Invalid song");
            return;
        }
        songs.Add(gsl.Songs[i]);
    }
    using MPlayer mp = new();
    foreach (GbaSong song in songs)
        mp.Add(song);
    await mp.ExecuteAsync();
});
