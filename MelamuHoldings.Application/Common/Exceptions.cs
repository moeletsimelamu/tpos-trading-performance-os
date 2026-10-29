namespace MelamuHoldings.Application.Common;

// The requested resource does not exist (HTTP 404)
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

// The request conflicts with existing state, e.g. a reused idempotency key or a duplicate name (HTTP 409)
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

// A database unique index rejected a write. Persistence raises it so use cases can react without knowing SQL Server.
public class UniqueConstraintViolationException : Exception
{
    public string IndexName { get; }

    public UniqueConstraintViolationException(string indexName, Exception inner)
        : base($"Unique index '{indexName}' was violated.", inner)
    {
        IndexName = indexName;
    }
}
