using System.Text.Json;

namespace norco;

public record Playlist(string Name, IReadOnlyList<JsonElement> Items);
