using Microsoft.AspNetCore.Diagnostics;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.API;

// Maps known failures to ProblemDetails; anything else stays a 500 handled by the default exception handler
public sealed class ProblemExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public ProblemExceptionHandler(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            DomainException => StatusCodes.Status422UnprocessableEntity,
            NotFoundException => StatusCodes.Status404NotFound,
            ConflictException => StatusCodes.Status409Conflict,
            _ => 0
        };
        if (status == 0) return false;

        httpContext.Response.StatusCode = status;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Title = exception.Message }
        });
    }
}
