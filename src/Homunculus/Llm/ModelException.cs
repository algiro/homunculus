namespace Homunculus.Llm;

/// <summary>The language model could not give a usable answer: it was unreachable, refused the call, or replied with something unreadable.</summary>
public sealed class ModelException : Exception
{
    public ModelException(string message) : base(message) { }

    public ModelException(string message, Exception inner) : base(message, inner) { }
}
