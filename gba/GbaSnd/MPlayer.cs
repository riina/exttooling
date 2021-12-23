namespace GbaSnd;

public sealed class MPlayer : IDisposable
{
    public readonly List<MSong> Songs;
    private readonly MPlayerContext _mPlayerContext;
    private int _index;

    public MPlayer()
    {
        _mPlayerContext = new MPlayerContext();
        Songs = new List<MSong>();
    }

    public async Task ExecuteAsync()
    {
        for (_index = 0; _index < Songs.Count;)
        {
            MSong song = Songs[_index];
            using MPlayerOutput p = _mPlayerContext.Stream(song.GetGenerator());
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
            if (Songs.Count(v => v == song) <= 1)
            {
                _index = Songs.IndexOf(song) + 1;
            }
            else
            {
                // TODO
            }
        }
    }

    public void Dispose() => _mPlayerContext.Dispose();
}
