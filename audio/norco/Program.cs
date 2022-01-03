// See https://aka.ms/new-console-template for more information

using CommandLine;
using norco;

await Parser.Default.ParseArguments<NorcoOptions>(args).WithParsedAsync(async opts =>
{
    using NorcoManager nm = new(opts);
    await nm.ExecuteAsync();
});
