// See https://aka.ms/new-console-template for more information

using norco;

var rootCommand = new NorcoRootCommand();
var parseResult = rootCommand.Parse(args);
parseResult.InvocationConfiguration.Output = Console.Error;
parseResult.InvocationConfiguration.Error = Console.Error;
return await parseResult.InvokeAsync();
