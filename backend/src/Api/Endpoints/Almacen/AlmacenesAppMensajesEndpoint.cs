using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Consumo;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// Recepción de mensajes de la app de Almacenes (contrato v1 §4-5, ACUERDO regla 5).
/// Auth: llave X-Millet-Almacenes-Key en tiempo constante contra
/// Integraciones:AlmacenesApp:ApiKey (patrón del nudge A+W): sin configurar o
/// incorrecta → 404. Acuse síncrono RECIBIDO/DUPLICADO/ERROR, nunca 202.
/// TI (5-nov) retiró este transporte: el intercambio será por la tabla <c>alm_mensaje</c> (ver
/// <see cref="AlmMensajeLector"/>); el endpoint queda con la llave sin configurar (404) y comparte manejador
/// (<see cref="AlmacenesAppProcesador"/>) con el lector. La bandeja de revisión de abajo sigue vigente.
/// </summary>
public static class AlmacenesAppMensajesEndpoint
{
    private const int MaxBytes = 1_000_000;
    private static readonly string[] TiposSalida = [.. AlmacenesAppSalida.Tipos.Keys, .. AlmacenesAppMerma.Tipos];

    private sealed record FilaBandeja(string MensajeId, string Tipo, string Estado, string? Referencia, string? Error, DateTimeOffset RecibidoAt, string Payload, string? Folio = null);

    public static IEndpointRouteBuilder MapAlmacenesAppMensajesEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/integraciones/almacenes/mensajes", async (
            HttpRequest request, IConfiguration configuration, AlmacenesAppProcesador procesador, CancellationToken ct) =>
        {
            var keyConfigurada = configuration["Integraciones:AlmacenesApp:ApiKey"];
            if (string.IsNullOrWhiteSpace(keyConfigurada))
                return Results.NotFound();
            if (!request.Headers.TryGetValue("X-Millet-Almacenes-Key", out var keyRecibida)
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(keyRecibida.ToString()),
                    Encoding.UTF8.GetBytes(keyConfigurada)))
                return Results.NotFound();

            static IResult Error(string? id, string msg, int status = 400) =>
                Results.Json(new AcuseApp(id, "ERROR", null, msg), statusCode: status);

            var headerId = request.Headers["X-Mensaje-Id"].ToString();

            if (request.ContentLength is > MaxBytes)
                return Error(headerId, "cuerpo demasiado grande", 413);

            JsonDocument doc;
            try { doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct); }
            catch (JsonException) { return Error(headerId, "sobre no es JSON válido"); }

            using (doc)
            {
                var root = doc.RootElement;
                var mensajeId = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("mensajeId", out var v)
                    && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (string.IsNullOrWhiteSpace(mensajeId) || mensajeId.Length > 64)
                    return Error(null, "mensajeId requerido (hasta 64)");
                if (headerId != mensajeId)
                    return Error(mensajeId, "X-Mensaje-Id no coincide con mensajeId del sobre");
                if (AlmacenesAppProcesador.ErrorDeSobre(root) is { } sobreMalo) return Error(mensajeId, sobreMalo);

                return Results.Json(await procesador.ProcesarAsync(root, ct));
            }
        })
        .AllowAnonymous()
        .WithTags("Integraciones Almacenes")
        .WithName("RecibirMensajeAlmacenesApp")
        .Produces<AcuseApp>(StatusCodes.Status200OK)
        .Produces<AcuseApp>(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // Bandeja de revisión (J6) y de excepciones (A4.3-a): salidas sin existencia suficiente (EN_REVISION; desde el 5-nov
        // acusadas ERROR a la app) y salidas rechazadas por datos (ERROR), ambas con su folio de revisión. Ninguna movió saldo.
        // A4.8: también los consumos de componentes por composición sin descontar (EN_REVISION: sin norma, factor,
        // sucursal, artículo o existencia; EXCEPCION: pieza sin componentes legibles, solo se descontó el vidrio).
        app.MapGet("/api/v1/almacen/mensajes-app/revision", async (AlmacenDbContext db, CancellationToken ct) =>
        {
            var mensajes = await db.MensajesApp.AsNoTracking()
                .Where(m => m.Estado == Millet.Almacen.Application.AppAlmacenes.SalidaAppAlmacenes.EnRevision
                    || (m.Estado == "ERROR" && TiposSalida.Contains(m.Tipo)))
                .OrderByDescending(m => m.RecibidoAt)
                .Select(m => new FilaBandeja(m.MensajeId, m.Tipo, m.Estado, m.Referencia, m.Error, m.RecibidoAt, m.Payload, m.Folio))
                .Take(500).ToListAsync(ct);
            var consumos = await db.ConsumosComponenteAw.AsNoTracking()
                .Where(c => c.Estado == ConsumoComponenteAw.EnRevision || c.Estado == ConsumoComponenteAw.Excepcion)
                .OrderByDescending(c => c.RegistradoAt).Take(500).ToListAsync(ct);
            return Results.Ok(mensajes.Concat(consumos.Select(c => new FilaBandeja(
                    $"{c.Origen}:{c.Etiqueta}:{c.Operacion}:{c.ComponenteRef}", ConsumoComponenteAw.Tipo, c.Estado,
                    $"Pedido {c.Auftnr} pos {c.Pos}", c.Error, c.RegistradoAt,
                    JsonSerializer.Serialize(new { etiqueta = c.Etiqueta, operacion = c.Operacion, componente = c.ComponenteRef, capas = c.Capas, anchoMm = c.AnchoMm, altoMm = c.AltoMm, sucursal = c.ClaveSucursal }))))
                .OrderByDescending(f => f.RecibidoAt).Take(500).ToList());
        })
        .RequireAuthorization(Millet.Api.Auth.PermissionPolicyProvider.Prefix
            + Millet.Identidad.Domain.PermisosCanonicos.AlmacenSalidasLeerTodas)
        .WithTags("Integraciones Almacenes")
        .WithName("ListarSalidasAppEnRevision");

        return app;
    }
}
