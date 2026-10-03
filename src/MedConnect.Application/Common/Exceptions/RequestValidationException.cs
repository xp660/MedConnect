using MedConnect.Application.Common.Validation;

namespace MedConnect.Application.Common.Exceptions;

public sealed class RequestValidationException : Exception
{
    public IReadOnlyList<ValidationError> Errors { get; }

    public RequestValidationException(IReadOnlyList<ValidationError> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }
}
