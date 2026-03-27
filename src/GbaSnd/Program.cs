using System.CommandLine;
using Playful.Gba;
using Playful.OpenTK;
using Playful.Player;

var rootCommand = new GbaSndRootCommand("Play songs from GBA file");
var parseResult = rootCommand.Parse(args);
parseResult.InvocationConfiguration.Output = Console.Error;
parseResult.InvocationConfiguration.Error = Console.Error;
return await parseResult.InvokeAsync();

class GbaSndRootCommand : RootCommand
{
    private readonly Argument<FileInfo> _inputFileArgument;
    private readonly Argument<List<int>> _idsArgument;
    private readonly Option<bool> _listOption;

    public GbaSndRootCommand(string description) : base(description)
    {
        _inputFileArgument = new Argument<FileInfo>("gba-file")
        {
            Description = "The input GBA file", //
            Arity = ArgumentArity.ExactlyOne //
        }.AcceptExistingOnly();
        Add(_inputFileArgument);
        _idsArgument = new Argument<List<int>>("song-id")
        {
            Description = "Specific song IDs to play", //
            Arity = ArgumentArity.ZeroOrMore //
        };
        Add(_idsArgument);
        _listOption = new Option<bool>("--list") { HelpName = "list-songs", Description = "List songs instead of playing" };
        Add(_listOption);
        SetAction(RunAsync);
    }

    private async Task<int> RunAsync(ParseResult parseResult)
    {
        FileInfo inputFileArgument = parseResult.GetRequiredValue(_inputFileArgument);
        List<int> ids = parseResult.GetValue(_idsArgument) ?? [];
        bool list = parseResult.GetValue(_listOption);
        if (!inputFileArgument.Exists)
        {
            WriteErrorLine($"{inputFileArgument.FullName}: does not exist");
            return 2;
        }
        GbaSongSource gsl;
        await using (FileStream fs = inputFileArgument.OpenRead())
        {
            gsl = new GbaSongSource(fs);
        }
        IReadOnlyList<GbaSong> songs;
        if (ids.Count == 0)
        {
            songs = gsl.Songs;
        }
        else
        {
            List<GbaSong> filteredSongs = new();
            foreach (int i in ids)
            {
                if (i < 0 || i >= gsl.Songs.Count)
                {
                    WriteErrorLine("Invalid song");
                    return 1;
                }
                filteredSongs.Add(gsl.Songs[i]);
            }
            songs = filteredSongs;
        }
        if (list)
        {
            foreach (var s in songs)
            {
                Console.WriteLine($"{s.Artist} - {s.Name}{(s.Duration is { } d ? $" ({d:mm\\:ss})" : "")}");
            }
            return 0;
        }
        using MPlayer mp = new(OpenALPlayerContext.Create);
        foreach (GbaSong song in songs)
        {
            mp.Add(song);
        }
        await mp.StartExecuteAsync();
        return 0;
    }

    private static void WriteErrorLine(string value)
    {
        Console.Error.WriteLine(value);
    }
}
