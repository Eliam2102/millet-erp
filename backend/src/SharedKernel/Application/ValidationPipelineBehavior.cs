using FluentValidation;
using MediatR;
using Millet.SharedKernel.Application.Exceptions;
using ValidationException = Millet.SharedKernel.Application.Exceptions.ValidationException;

namespace Millet.SharedKernel.Application;

/// <summary>
/// Pipeline behavior de MediatR que ejecuta los <see cref="IValidator{T}"/>
/// registrados para cada request antes de llamar al handler. Si hay
/// errores, lanza <see cref="ValidationException"/> que el
/// <c>GlobalExceptionHandler</c> traduce a HTTP 400 con shape estándar
/// (ADR-0010, ADR-0018).
///
/// Sin este behavior, los handlers reciben el request directamente y
/// los validators registrados (vía <see cref="MilletApplicationServiceCollectionExtensions"/>)
/// nunca se ejecutan en runtime — solo son invocables a mano. Esto
/// causaría que las invariantes del dominio (que mapean a 422) sirvan
/// de única protección, perdiendo la distinción 400/422 (cuidado §13.1).
/// </summary>
public sealed class ValidationPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationPipelineBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
        if (failures.Count > 0)
        {
            var errors = failures
                .Select(f => new ValidationError(
                    f.PropertyName,
                    f.ErrorCode ?? "VALIDATION_ERROR",
                    f.ErrorMessage))
                .ToArray();
            throw new ValidationException(errors);
        }

        return await next();
    }
}
