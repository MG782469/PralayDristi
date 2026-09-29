namespace PralayDrishti.Domain.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

public sealed class NotFoundException : DomainException
{
    public NotFoundException(string entity, object key) : base($"{entity} '{key}' was not found.") { }
}

public sealed class ConflictException : DomainException
{
    public ConflictException(string message) : base(message) { }
}

public sealed class ForbiddenAccessException : DomainException
{
    public ForbiddenAccessException(string message = "You are not allowed to do this.") : base(message) { }
}

/// <summary>Request is well-formed but breaks a rule (registration closed, run already finished...).</summary>
public sealed class BusinessRuleException : DomainException
{
    public BusinessRuleException(string message) : base(message) { }
}
