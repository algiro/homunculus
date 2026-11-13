namespace Homunculus.Llm;

public enum Role { System, User, Assistant }

public sealed record Message(Role Role, string? Content = null)
{
    public static Message System(string content) => new(Role.System, content);
    public static Message User(string content) => new(Role.User, content);
    public static Message Assistant(string? content) => new(Role.Assistant, content);
}
