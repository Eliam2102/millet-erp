using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Parametros;

/// <summary>
/// Command para actualizar el valor de un parámetro global. La clave
/// y el tipo son inmutables (cambios de shape requieren migración);
/// solo el valor se PATCHea aquí. El dominio valida el nuevo valor
/// contra el tipo declarado y throw <c>BusinessRuleException</c> si
/// no parsea (422).
///
/// <para>404 si la clave no existe.</para>
/// </summary>
public sealed record ActualizarParametroCommand(string Clave, string Valor) : IRequest<ParametroResponse>;

public sealed class ActualizarParametroCommandValidator : AbstractValidator<ActualizarParametroCommand>
{
    public ActualizarParametroCommandValidator()
    {
        RuleFor(x => x.Clave)
            .NotEmpty().WithMessage("La clave es requerida.")
            .MaximumLength(100);
        RuleFor(x => x.Valor)
            .NotNull().WithMessage("El valor es requerido (use string vacío si aplica).")
            .MaximumLength(2000);
    }
}

public sealed class ActualizarParametroHandler
    : IRequestHandler<ActualizarParametroCommand, ParametroResponse>
{
    private readonly CompartidoDbContext _db;

    public ActualizarParametroHandler(CompartidoDbContext db)
    {
        _db = db;
    }

    public async Task<ParametroResponse> Handle(
        ActualizarParametroCommand command,
        CancellationToken cancellationToken)
    {
        var row = await _db.ParametrosGlobales
            .FirstOrDefaultAsync(p => p.Clave == command.Clave, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PARAMETRO_GLOBAL_NO_ENCONTRADO",
                $"No existe un parámetro global con clave '{command.Clave}'.");

        // El dominio valida que el valor parsee según el Tipo.
        row.ActualizarValor(command.Valor);

        await _db.SaveChangesAsync(cancellationToken);

        return new ParametroResponse(
            row.Id, row.Clave, row.Valor, row.Tipo, row.Modulo, row.Descripcion, row.Version);
    }
}
