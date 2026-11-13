namespace Homunculus.Tools;

/// <summary>A text parameter a tool accepts.</summary>
public sealed record Parameter(string Name, string Description, bool IsRequired)
{
    public static Parameter Required(string name, string description) => new(name, description, true);

    public static Parameter Optional(string name, string description) => new(name, description, false);
}
