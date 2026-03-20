namespace Playful;

[AttributeUsage(AttributeTargets.Class)]
public class SongLoaderInfoAttribute : Attribute
{
    public string Name { get; set; }

    public SongLoaderInfoAttribute(string name) => Name = name;
}
