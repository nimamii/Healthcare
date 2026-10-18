namespace Healthcare.SharedKernel;

/// <summary>
/// Thrown when a domain invariant is violated.
/// These are bugs, not expected outcomes — if a caller is throwing these
/// routinely, the API is missing a Result-returning path.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>An aggregate was asked to move to a state its rules forbid.</summary>
public sealed class InvalidStateTransitionException : DomainException
{
    public InvalidStateTransitionException(string entity, string from, string to)
        : base(
            "invalid_state_transition",
            $"{entity} cannot move from {from} to {to}.")
    {
        Entity = entity;
        From = from;
        To = to;
    }

    public string Entity { get; }

    public string From { get; }

    public string To { get; }
}
