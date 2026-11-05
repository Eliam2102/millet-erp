using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Compras.Application.Integration.AlmCompra;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Compras;

/// <summary>
/// Bandeja ERP → Almacenes (86e3jvu83 · C3.2): estado de cada COMPRA_RECIBIDA / COMPRA_CANCELADA / COMPRA_MODIFICADA y reintento
/// de lo que no llegó a escribirse. El tablero (I13.6) es la segunda rebanada; esto es solo la lectura y el reintento.
/// Reutiliza <c>compras.ordenes.leer</c> y <c>compras.ordenes.logistica</c> (sin permisos nuevos).
/// </summary>
public static class AlmCompraEnviosEndpoints
{
    public sealed record AlmCompraEnvioResponse(
        Guid Id, Guid OrdenCompraId, string Referencia, long Secuencia, string MensajeId, string Tipo, string Estado,
        int Intentos, DateTimeOffset? ProximoIntentoEn, string? UltimoError, string? CompraIdAlmacenes,
        DateTimeOffset? EnviadoEn, DateTimeOffset? AcuseEn, DateTimeOffset CreatedAt);

    public static IEndpointRouteBuilder MapAlmCompraEnviosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/compras/integraciones/alm-compra/envios").WithTags("Compras");

        group.MapGet("/", async (
            [FromQuery] string? estado, [FromQuery] Guid? ordenCompraId, ComprasDbContext db, CancellationToken ct) =>
        {
            var q = db.AlmCompraEnvios.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(estado)) q = q.Where(e => e.Estado == estado);
            if (ordenCompraId is Guid oc) q = q.Where(e => e.OrdenCompraId == oc);
            var filas = await q.OrderByDescending(e => e.Secuencia).Take(200)
                .Select(e => new AlmCompraEnvioResponse(e.Id, e.OrdenCompraId, e.Referencia, e.Secuencia, e.MensajeId, e.Tipo,
                    e.Estado, e.Intentos, e.ProximoIntentoEn, e.UltimoError, e.CompraIdAlmacenes, e.EnviadoEn, e.AcuseEn, e.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(filas);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesLeer)
        .WithName("ListarAlmCompraEnvios")
        .WithSummary("Mensajes de compras hacia Almacenes y su acuse (más recientes primero, máx. 200)");

        group.MapPost("/{id:guid}/reintentar", async (
            Guid id, ComprasDbContext db, AlmCompraEncolador encolador, CancellationToken ct) =>
        {
            var envio = await db.AlmCompraEnvios.FirstOrDefaultAsync(e => e.Id == id, ct)
                ?? throw new EntityNotFoundException("ALM_COMPRA_ENVIO_NO_ENCONTRADO", "No existe ese mensaje.");
            string? sobre = null;
            if (envio.Sobre.Length == 0)
                sobre = await encolador.ReconstruirSobreAsync(envio, ct)
                    ?? throw new BusinessRuleException("ALM_COMPRA_SIN_LINEAS_APP", "La OC ya no tiene líneas que surtan una requisición de la app.");
            envio.Reintentar(sobre);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesLogistica)
        .WithName("ReintentarAlmCompraEnvio")
        .WithSummary("Reintenta un mensaje que no llegó a escribirse en Almacenes (error local o reintentos agotados)");

        return app;
    }
}
