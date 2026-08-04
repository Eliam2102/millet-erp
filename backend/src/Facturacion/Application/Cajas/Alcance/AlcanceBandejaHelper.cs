using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Pedidos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas.Alcance;

/// <summary>
/// Lógica común de las bandejas bajo Capa A (12-cajas.md §9): aplica el
/// alcance del usuario al <c>IQueryable</c> ya filtrado por los criterios de
/// la bandeja y, para usuarios con <c>leer-todas</c>, calcula
/// <c>sinAsignarCount</c> (badge `[Decisión 12-B]`) y opcionalmente restringe
/// al bucket "Sin asignar" (<c>?alcance=sin-asignar</c>).
/// </summary>
public static class AlcanceBandejaHelper
{
    public static Task<(IQueryable<T> Query, int? SinAsignarCount)> AplicarAsync<T>(
        IAlcanceCajaEvaluator evaluator,
        IQueryable<T> query,
        bool soloSinAsignar,
        CancellationToken cancellationToken) where T : Comprobante
        => AplicarCoreAsync(
            evaluator, query, soloSinAsignar,
            (alcance, q) => alcance.AplicarA(q),
            (sinAsignar, q) => sinAsignar.AplicarA(q),
            cancellationToken);

    public static Task<(IQueryable<PedidoFacturable> Query, int? SinAsignarCount)> AplicarAsync(
        IAlcanceCajaEvaluator evaluator,
        IQueryable<PedidoFacturable> query,
        bool soloSinAsignar,
        CancellationToken cancellationToken)
        => AplicarCoreAsync(
            evaluator, query, soloSinAsignar,
            (alcance, q) => alcance.AplicarA(q),
            (sinAsignar, q) => sinAsignar.AplicarA(q),
            cancellationToken);

    private static async Task<(IQueryable<T> Query, int? SinAsignarCount)> AplicarCoreAsync<T>(
        IAlcanceCajaEvaluator evaluator,
        IQueryable<T> query,
        bool soloSinAsignar,
        Func<AlcanceCajas, IQueryable<T>, IQueryable<T>> aplicarAlcance,
        Func<AlcanceSinAsignar, IQueryable<T>, IQueryable<T>> aplicarSinAsignar,
        CancellationToken cancellationToken)
    {
        var alcance = await evaluator.ResolverAsync(cancellationToken);

        if (soloSinAsignar && !alcance.EsTotal)
            throw new ForbiddenException(
                "CAJA_SIN_ASIGNAR_PROHIBIDO",
                "El bucket \"Sin asignar\" requiere el permiso facturacion.caja.leer-todas.");

        if (!alcance.EsTotal)
            return (aplicarAlcance(alcance, query), null);

        var sinAsignar = await evaluator.ResolverSinAsignarAsync(cancellationToken);
        var bucket = aplicarSinAsignar(sinAsignar, query);
        var sinAsignarCount = await bucket.CountAsync(cancellationToken);

        return (soloSinAsignar ? bucket : query, sinAsignarCount);
    }
}
