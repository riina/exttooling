using System.CommandLine;

namespace norco;

public sealed class NorcoRootCommand : RootCommand
{
    public NorcoRootCommand() : this("play music etc.")
    {
    }

    public NorcoRootCommand(string description) : base(description)
    {
        NorcoPlayCommandBase norcoPlayCommandBase = new();
        norcoPlayCommandBase.AddToCommand(this);
        SetAction(norcoPlayCommandBase.RunAsync);
    }
}
