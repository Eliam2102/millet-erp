using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Saldos;
using Millet.Api.Auth;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.Saldos;

/// <summary>
/// Endpoints de Saldos del módulo Almacén (F2-PR2). Bajo
/// <c>/api/v1/almacen/saldos</c>. Read-only para el FE — para
/// consumidores cross-módulo usar <c>IAlmacenSaldoQueryPort</c>
/// (open host service).
/// </summary>
public static class SaldosEndpoints
{
    private const int LimitMax = 500;

    public static IEndpointRouteBuilder MapSaldosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/almacen/saldos")
            .WithTags("Almacen")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] Guid? subAlmacenId,
            [FromQuery] Guid? articuloId,
            [FromQuery] bool? soloConStock,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            var response = await mediator.Send(
                new ListarSaldosQuery(subAlmacenId, articuloId, soloConStock ?? false, off, lim), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAlmacenesRead)
        .WithName("ListarSaldos")
        .WithSummary("Listar saldos materializados de inventario (F2-PR2)")
        .Produces<AlmacenPagedResponse<SaldoListItem>>(StatusCodes.Status200OK);

        // C7.2b: puebla el selector de bin en salidas — ubicaciones con
        // existencia (>0) del artículo, incluida la ÚNICA. Salida-por-línea C2:
        // subAlmacenId opcional — sin él, lista bins de todos los subs.
        group.MapGet("/por-ubicacion", async (
            [FromQuery] Guid articuloId,
            [FromQuery] Guid? subAlmacenId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new SaldosPorUbicacionQuery(articuloId, subAlmacenId), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAlmacenesRead)
        .WithName("SaldosPorUbicacion")
        .WithSummary("Saldos por ubicación de un artículo, para el selector de salida (C7.2b)")
        .Produces<IReadOnlyList<SaldoUbicacionItem>>(StatusCodes.Status200OK);

        // PR6 (ADR-0047): consulta jerárquica — hijos lazy de un nodo con
        // rollup (cantidad + valor aditivos; el CPP NO se promedia). Modo
        // artículo = articuloId presente; modo ubicación = sin articuloId
        // (la ubicación expande a artículos).
        group.MapGet("/jerarquia", async (
            [FromQuery] string? nodoTipo,
            [FromQuery] Guid? nodoId,
            [FromQuery] Guid? articuloId,
            [FromQuery] bool? incluirVacios,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (!TryParseNodoTipo(nodoTipo, out var tipo))
                return Results.Problem(
                    title: "nodoTipo inválido",
                    detail: "nodoTipo debe ser raiz|sucursal|almacen|subAlmacen|ubicacion.",
                    statusCode: StatusCodes.Status400BadRequest);

            var response = await mediator.Send(
                new ObtenerHijosJerarquiaQuery(tipo, nodoId, articuloId, incluirVacios ?? false), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAlmacenesRead)
        .WithName("SaldosJerarquia")
        .WithSummary("Hijos de un nodo de la jerarquía de saldos, con rollup por nodo (PR6)")
        .Produces<IReadOnlyList<NodoJerarquiaDto>>(StatusCodes.Status200OK);

        return app;
    }

    /// <summary>Omitido/vacío = raíz. Acepta nombre del enum, case-insensitive.</summary>
    private static bool TryParseNodoTipo(string? valor, out NivelNodoJerarquia tipo)
    {
        if (string.IsNullOrEmpty(valor))
        {
            tipo = NivelNodoJerarquia.Raiz;
            return true;
        }
        return Enum.TryParse(valor, ignoreCase: true, out tipo) && Enum.IsDefined(tipo);
    }

    private static (int off, int lim) NormalizePaging(int? offset, int? limit)
    {
        var off = offset is < 0 ? 0 : offset ?? 0;
        var lim = limit is null or <= 0 ? 50 : Math.Min(limit.Value, LimitMax);
        return (off, lim);
    }
}
