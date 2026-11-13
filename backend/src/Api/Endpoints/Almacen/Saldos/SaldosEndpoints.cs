using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Saldos;
using Millet.Api.Auth;
using Millet.Identidad.Domain;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Api.Web;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

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

        // UX-F2-ALM-05: totales (valor MXN; cantidad solo con una única UM) de todo el
        // conjunto filtrado, con los mismos filtros y alcance que el listado.
        group.MapGet("/totales", async (
            [FromQuery] Guid? subAlmacenId,
            [FromQuery] Guid? articuloId,
            [FromQuery] bool? soloConStock,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ObtenerTotalesSaldosQuery(subAlmacenId, articuloId, soloConStock ?? false), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAlmacenesRead)
        .WithName("TotalesSaldos")
        .WithSummary("Totales de saldos del conjunto filtrado (UX-F2-ALM-05)")
        .Produces<SaldosTotalesResponse>(StatusCodes.Status200OK);

        // C7.2b: puebla el selector de bin en salidas — ubicaciones con
        // existencia (>0) del artículo, incluida la ÚNICA. Salida-por-línea C2:
        // subAlmacenId opcional — sin él, lista bins de todos los subs.
        group.MapGet("/por-ubicacion", async (
            [FromQuery] Guid articuloId,
            [FromQuery] Guid? subAlmacenId,
            [FromQuery] Guid? varianteId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new SaldosPorUbicacionQuery(articuloId, subAlmacenId, varianteId), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAlmacenesRead)
        .WithName("SaldosPorUbicacion")
        .WithSummary("Saldos por ubicación de un artículo, para el selector de salida (C7.2b)")
        .Produces<IReadOnlyList<SaldoUbicacionItem>>(StatusCodes.Status200OK);

        // M-231: traslado de hojas entre posiciones del mismo almacén (salida + entrada ligadas, sin póliza). 409 si el saldo ya cambió.
        group.MapPost("/traslados-posicion", async (
            Millet.Almacen.Application.Saldos.TrasladarPosicion req,
            Millet.Almacen.Infrastructure.Persistence.AlmacenDbContext db,
            Millet.Almacen.Domain.Ports.IVarianteVidrioReadPort variantes,
            Millet.Almacen.Domain.Ports.IArticuloReadPort articulos,
            Millet.Almacen.Domain.Ports.IPeriodoContableReadPort periodo,
            DocumentoSucursalScope scope,
            ICurrentUserContext user,
            ICurrentEmpresaContext empresa,
            CancellationToken ct) =>
        {
            var subAlmacenId = await db.SaldosInventario.AsNoTracking().Where(s => s.Id == req.SaldoId).Select(s => (Guid?)s.SubAlmacenId).FirstOrDefaultAsync(ct)
                ?? throw new EntityNotFoundException("SALDO_NO_ENCONTRADO", "La existencia elegida ya no está; recarga la pantalla.");
            var (_, sucursalId) = await Millet.Almacen.Application.Catalogo.AlmacenSucursalResolver.ResolverAsync(db, subAlmacenId, ct);
            if (sucursalId is Guid suc) await scope.VerificarSucursalAsync(suc, PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales, ct);
            await using var tx = db.Database.IsNpgsql() ? await db.Database.BeginTransactionAsync(ct) : null;
            var r = await Millet.Almacen.Application.Saldos.TrasladoPosicionApp.RegistrarAsync(db, variantes, articulos, periodo,
                empresa.Current ?? throw new BusinessRuleException("TRASLADO_SIN_EMPRESA", "El contexto de empresa es requerido."),
                user.UserId ?? Guid.Empty, req, ct);
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return Results.Ok(r);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasRegistrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("TrasladarEntrePosiciones")
        .Produces<Millet.Almacen.Application.Saldos.TrasladoPosicionResultado>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

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
