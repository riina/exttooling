namespace Playful;

[AttributeUsage(AttributeTargets.Class)]
public class ReferenceNameAttribute : Attribute
{
    public string Name { get; set; }

    public ReferenceNameAttribute(string name)
    {
        Name = name;
    }
}
