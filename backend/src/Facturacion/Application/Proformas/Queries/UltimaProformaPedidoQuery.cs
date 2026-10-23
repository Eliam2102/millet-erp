using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Proformas.Queries;

/// <summary>
/// Última proforma (versión más alta) de un pedido facturable (U1.8a). Un
/// pedido fuera del alcance de cajas responde 404; un pedido sin proformas
/// devuelve <c>null</c> (204): es el estado normal antes de la primera.
/// </summary>
public sealed record UltimaProformaPedidoQuery(Guid PedidoFacturableId) : IRequest<ProformaPedidoResponse?>;

public sealed class UltimaProformaPedidoValidator : AbstractValidator<UltimaProformaPedidoQuery>
{
    public UltimaProformaPedidoValidator()
    {
        RuleFor(q => q.PedidoFacturableId).NotEmpty();
    }
}

public sealed class UltimaProformaPedidoHandler
    : IRequestHandler<UltimaProformaPedidoQuery, ProformaPedidoResponse?>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;

    public UltimaProformaPedidoHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    {
        _db = db;
        _alcance = alcance;
    }

    public async Task<ProformaPedidoResponse?> Handle(
        UltimaProformaPedidoQuery query, CancellationToken cancellationToken)
    {
        await ProformaAlcance.AsegurarPedidoVisibleAsync(_db, _alcance, query.PedidoFacturableId, cancellationToken);

        var proforma = await _db.ProformasPedido.AsNoTracking()
            .Where(p => p.PedidoFacturableId == query.PedidoFacturableId)
            .OrderByDescending(p => p.NumeroVersion)
            .FirstOrDefaultAsync(cancellationToken);

        return proforma is null ? null : ProformaPedidoResponse.Desde(proforma);
    }
}

/// <summary>Alcance de cajas sobre el pedido de la proforma (fuera de alcance = mismo 404 que inexistente).</summary>
internal static class ProformaAlcance
{
    public static async Task<Millet.Facturacion.Domain.Pedidos.PedidoFacturable> AsegurarPedidoVisibleAsync(
        FacturacionDbContext db, IAlcanceCajaEvaluator evaluador, Guid pedidoId, CancellationToken cancellationToken)
    {
        var alcance = await evaluador.ResolverAsync(cancellationToken);
        return await alcance.AplicarA(db.PedidosFacturables.AsNoTracking())
            .FirstOrDefaultAsync(p => p.Id == pedidoId, cancellationToken)
            ?? throw new EntityNotFoundException("PEDIDO_NO_ENCONTRADO", $"No existe el pedido facturable '{pedidoId}'.");
    }
}
