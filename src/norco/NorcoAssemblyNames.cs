using System.Text.Json;

namespace norco;

internal static class NorcoAssemblyNames
{
    private const string FileName = "norco.json";
    private static readonly string s_filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
    private static List<string>? s_assemblyNames;

    public static IReadOnlyCollection<string> GetNames()
    {
        if (s_assemblyNames != null) return s_assemblyNames;
        s_assemblyNames = new List<string>();
        try
        {
            List<string>? entries = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(s_filePath));
            if (entries != null)
                s_assemblyNames.AddRange(entries);
        }
        catch
        {
            // ignored
        }
        return s_assemblyNames;
    }
}
