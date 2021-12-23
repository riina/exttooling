using GbaSnd;

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
    GbaSongLoader gsl;
    await using (FileStream fs = File.OpenRead(gba)) gsl = new GbaSongLoader(fs);
    if (!indices.Any())
    {
        foreach (var s in gsl.Songs)
        {
            Console.WriteLine(s.Name);
        }
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
    mp.Songs.AddRange(songs);
    await mp.ExecuteAsync();
});
