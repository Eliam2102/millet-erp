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
        if (ParametrosUmbralesConteo.Claves.Contains(command.Clave))
            return await ActualizarUmbralAsync(command, cancellationToken);

        var row = await _db.ParametrosGlobales
            .FirstOrDefaultAsync(p => p.Clave == command.Clave, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PARAMETRO_GLOBAL_NO_ENCONTRADO",
                $"No existe un parámetro global con clave '{command.Clave}'.");

        // El dominio valida que el valor parsee según el Tipo.
        if (command.Clave == Millet.SharedKernel.Application.Calendario.CalendarioHabil.ClaveFestivos)
            _ = Millet.SharedKernel.Application.Calendario.CalendarioHabil.LeerFestivos(command.Valor);
        row.ActualizarValor(command.Valor);

        await _db.SaveChangesAsync(cancellationToken);

        return new ParametroResponse(
            row.Id, row.Clave, row.Valor, row.Tipo, row.Modulo, row.Descripcion, row.Version);
    }

    private async Task<ParametroResponse> ActualizarUmbralAsync(
        ActualizarParametroCommand command, CancellationToken cancellationToken)
    {
        // Serializa los PATCH de la política completa: dos cambios simultáneos
        // no pueden validar contra límites antiguos y dejar Nivel 1 >= Nivel 2.
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        var claves = ParametrosUmbralesConteo.Claves.ToArray();
        var filas = await _db.ParametrosGlobales.FromSqlInterpolated($"""
            SELECT * FROM compartido.parametros_globales
            WHERE clave = ANY ({claves}) ORDER BY clave FOR UPDATE
            """).ToListAsync(cancellationToken);
        var row = filas.SingleOrDefault(p => p.Clave == command.Clave)
            ?? throw new EntityNotFoundException("PARAMETRO_GLOBAL_NO_ENCONTRADO",
                $"No existe un parámetro global con clave '{command.Clave}'.");
        var valores = filas.ToDictionary(p => p.Clave, p => p.Valor);
        valores[command.Clave] = command.Valor;
        ParametrosUmbralesConteo.Leer(valores);
        row.ActualizarValor(command.Valor);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new ParametroResponse(
            row.Id, row.Clave, row.Valor, row.Tipo, row.Modulo, row.Descripcion, row.Version);
    }

}
