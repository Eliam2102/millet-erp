using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.Catalogos;

/// <summary>
/// Reclasifica la <see cref="Naturaleza"/> de un batch de artículos en
/// <c>compartido.articulos</c> (F9-PR1). Usado post-go-live cuando el
/// cliente decide ajustar la matriz de aprobación A1 §3.bis.1 (artículos
/// originalmente <c>Estandar</c> que pasan a <c>Critico</c>, etc).
///
/// <para>
/// Single SQL via <see cref="EntityFrameworkQueryableExtensions.ExecuteUpdateAsync{TSource}"/>:
/// O(1) round-trip a Postgres independiente del tamaño del batch. Tope
/// de 500 IDs por request para evitar locks excesivos sobre el catálogo
/// y request bodies grandes.
/// </para>
/// </summary>
public sealed record ReclasificarNaturalezaArticulosCommand(
    IReadOnlyList<Guid> ArticuloIds,
    Naturaleza Naturaleza,
    string? Motivo) : IRequest<ReclasificarNaturalezaArticulosResponse>;

public sealed record ReclasificarNaturalezaArticulosResponse(int Reclasificados);

public sealed class ReclasificarNaturalezaArticulosHandler
    : IRequestHandler<ReclasificarNaturalezaArticulosCommand, ReclasificarNaturalezaArticulosResponse>
{
    public const int MaxBatchSize = 500;

    private readonly CompartidoDbContext _db;

    public ReclasificarNaturalezaArticulosHandler(CompartidoDbContext db)
    {
        _db = db;
    }

    public async Task<ReclasificarNaturalezaArticulosResponse> Handle(
        ReclasificarNaturalezaArticulosCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        if (command.ArticuloIds.Count == 0)
        {
            throw new BusinessRuleException(
                "RECLASIFICAR_SIN_IDS",
                "Debe proveer al menos un articuloId.");
        }
        if (command.ArticuloIds.Count > MaxBatchSize)
        {
            throw new BusinessRuleException(
                "RECLASIFICAR_BATCH_DEMASIADO_GRANDE",
                $"El batch excede el tope de {MaxBatchSize} ids; particione la operación.");
        }

        var idSet = command.ArticuloIds.ToHashSet();
        var nuevoValor = command.Naturaleza;

        var afectados = await _db.Articulos
            .Where(a => idSet.Contains(a.Id))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(a => a.Naturaleza, nuevoValor),
                ct);

        return new ReclasificarNaturalezaArticulosResponse(afectados);
    }
}

public sealed class ReclasificarNaturalezaArticulosValidator
    : AbstractValidator<ReclasificarNaturalezaArticulosCommand>
{
    public ReclasificarNaturalezaArticulosValidator()
    {
        RuleFor(c => c.ArticuloIds).NotEmpty();
        RuleFor(c => c.Naturaleza).IsInEnum();
        RuleFor(c => c.Motivo).MaximumLength(500);
    }
}
