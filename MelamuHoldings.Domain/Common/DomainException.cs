namespace MelamuHoldings.Domain.Common;

// A business rule was violated. The API maps these to 422 responses once finance endpoints exist (Phase 4).
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
