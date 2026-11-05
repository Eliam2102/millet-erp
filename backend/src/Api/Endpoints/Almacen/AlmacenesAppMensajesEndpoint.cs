using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Consumo;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// Recepción de mensajes de la app de Almacenes (contrato v1 §4-5, ACUERDO regla 5).
/// Auth: llave X-Millet-Almacenes-Key en tiempo constante contra
/// Integraciones:AlmacenesApp:ApiKey (patrón del nudge A+W): sin configurar o
/// incorrecta → 404. Acuse síncrono RECIBIDO/DUPLICADO/ERROR, nunca 202.
/// </summary>
public static class AlmacenesAppMensajesEndpoint
{
    private const int MaxBytes = 1_000_000;
    private static readonly string[] TiposSalida = [.. AlmacenesAppSalida.Tipos.Keys, .. AlmacenesAppMerma.Tipos];

    public sealed record Acuse(string? MensajeId, string Estado, string? Folio, string? Error);

    private sealed record FilaBandeja(string MensajeId, string Tipo, string Estado, string? Referencia, string? Error, DateTimeOffset RecibidoAt, string Payload);

    public static IEndpointRouteBuilder MapAlmacenesAppMensajesEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/integraciones/almacenes/mensajes", async (
            HttpRequest request, IConfiguration configuration, AlmacenDbContext db,
            Millet.Almacen.Domain.Ports.IComprasCrearRqAppPort rqApp,
            Millet.Almacen.Domain.Ports.IAppSalidaResolverPort salidaResolver,
            Millet.Almacen.Domain.Ports.IUsuarioServicioReadPort usuarioServicio,
            Millet.Almacen.Infrastructure.PublicAdapters.SalidaAppAlmacenesGuardado salidasGuardado,
            Millet.Almacen.Infrastructure.PublicAdapters.MermaProcesoAppGuardado mermasGuardado, CancellationToken ct) =>
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
                Results.Json(new Acuse(id, "ERROR", null, msg), statusCode: status);

            var headerId = request.Headers["X-Mensaje-Id"].ToString();

            if (request.ContentLength is > MaxBytes)
                return Error(headerId, "cuerpo demasiado grande", 413);

            JsonDocument doc;
            try { doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct); }
            catch (JsonException) { return Error(headerId, "sobre no es JSON válido"); }

            using (doc)
            {
                var root = doc.RootElement;
                string? Str(string n) => root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() : null;

                var mensajeId = Str("mensajeId");
                var tipo = Str("tipo");
                if (string.IsNullOrWhiteSpace(mensajeId) || mensajeId.Length > 64)
                    return Error(null, "mensajeId requerido (hasta 64)");
                if (headerId != mensajeId)
                    return Error(mensajeId, "X-Mensaje-Id no coincide con mensajeId del sobre");
                if (string.IsNullOrWhiteSpace(tipo) || tipo.Length > 100
                    || !root.TryGetProperty("datos", out var datos) || datos.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("version", out var ver) || ver.ValueKind != JsonValueKind.Number)
                    return Error(mensajeId, "sobre inválido: tipo, version y datos son requeridos");

                var previo = await db.MensajesApp.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.MensajeId == mensajeId, ct);
                // A4.3-a: una salida rechazada quedó en la bandeja sin mover saldo; cuando la app reenvía el
                // mismo mensajeId (ya corregido el dato en el ERP) se procesa otra vez y sale de la bandeja.
                if (previo is { Estado: "ERROR" } && TiposSalida.Contains(previo.Tipo))
                {
                    await db.MensajesApp.Where(m => m.MensajeId == mensajeId && m.Estado == "ERROR").ExecuteDeleteAsync(ct);
                    previo = null;
                }
                if (previo is null)
                {
                    var referencia = Str("referencia");
                    string estado; string? folio, error;
                    if (AlmacenesAppSalida.Tipos.ContainsKey(tipo))
                    {
                        // Las salidas mueven existencia: movimiento y MensajeApp se confirman juntos en el guardado.
                        var (resuelta, errorSalida) = await AlmacenesAppSalida.ManejarAsync(tipo, datos, salidaResolver, usuarioServicio, ct);
                        // Un ERROR de datos (artículo/medida/sucursal sin dar de alta, campo inválido) no descuenta nada
                        // y queda en la bandeja de excepciones (A4.3-a) con estado ERROR; la app lo corrige y reenvía
                        // el mismo mensajeId, que se vuelve a procesar (ver arriba).
                        if (resuelta is null)
                        {
                            db.MensajesApp.Add(new MensajeApp(mensajeId, tipo,
                                referencia?.Length > 100 ? referencia[..100] : referencia, root.GetRawText(), "ERROR", null,
                                errorSalida?.Length > 1000 ? errorSalida[..1000] : errorSalida));
                            try { await db.SaveChangesAsync(ct); }
                            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                            {
                                db.ChangeTracker.Clear(); // otro envío del mismo mensajeId ganó la carrera; el acuse sigue siendo ERROR
                            }
                            return Results.Json(new Acuse(mensajeId, "ERROR", null, errorSalida));
                        }
                        var g = await salidasGuardado.GuardarAsync(mensajeId, tipo, referencia, root.GetRawText(),
                            resuelta.EmpresaId, resuelta.UsuarioId, resuelta.Salidas, ct);
                        if (g.Estado == "ERROR")
                            return Results.Json(new Acuse(mensajeId, g.Estado, g.Folio, g.Error));
                        if (!g.Duplicado)
                            // Salida sin existencia: se acepta (RECIBIDO) y queda en la bandeja de revisión (estado guardado).
                            return g.Estado == Millet.Almacen.Application.AppAlmacenes.SalidaAppAlmacenes.EnRevision
                                ? Results.Json(new Acuse(mensajeId, "RECIBIDO", null, null))
                                : Results.Json(new Acuse(mensajeId, g.Estado, g.Folio, g.Error));
                        previo = await db.MensajesApp.AsNoTracking().FirstAsync(m => m.MensajeId == mensajeId, ct);
                    }
                    else if (AlmacenesAppMerma.Tipos.Contains(tipo))
                    {
                        // A4.4: desperdicio de mesa y merma de proceso; el desperdicio avisa a Contabilidad, la merma queda pendiente.
                        var (resuelta, errorMerma) = await AlmacenesAppMerma.ManejarAsync(tipo, datos, salidaResolver, usuarioServicio, ct);
                        if (resuelta is null)
                        {
                            db.MensajesApp.Add(new MensajeApp(mensajeId, tipo,
                                referencia?.Length > 100 ? referencia[..100] : referencia, root.GetRawText(), "ERROR", null,
                                errorMerma?.Length > 1000 ? errorMerma[..1000] : errorMerma));
                            try { await db.SaveChangesAsync(ct); }
                            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                            {
                                db.ChangeTracker.Clear();
                            }
                            return Results.Json(new Acuse(mensajeId, "ERROR", null, errorMerma));
                        }
                        var g = await mermasGuardado.GuardarAsync(mensajeId, tipo, referencia, root.GetRawText(),
                            resuelta.EmpresaId, resuelta.Merma, ct);
                        if (!g.Duplicado) return Results.Json(new Acuse(mensajeId, g.Estado, null, g.Error));
                        previo = await db.MensajesApp.AsNoTracking().FirstAsync(m => m.MensajeId == mensajeId, ct);
                    }
                    else
                    {
                        // Punto de enchufe de handlers: un case por tipo.
                        (estado, folio, error) = tipo switch
                        {
                            "REQUISICION_ENVIADA" => await AlmacenesAppRequisicionEnviada.ManejarAsync(datos, rqApp, ct),
                            _ => ("ERROR", (string?)null, (string?)"tipo aún no soportado"),
                        };
                        if (tipo == "REQUISICION_ENVIADA" && estado == "ERROR")
                            return Results.Json(new Acuse(mensajeId, estado, folio, error));
                        db.MensajesApp.Add(new MensajeApp(mensajeId, tipo,
                            referencia?.Length > 100 ? referencia[..100] : referencia,
                            root.GetRawText(), estado, folio, error));
                        try
                        {
                            await db.SaveChangesAsync(ct);
                            return Results.Json(new Acuse(mensajeId, estado, folio, error));
                        }
                        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                        {
                            db.ChangeTracker.Clear();
                            previo = await db.MensajesApp.AsNoTracking().FirstAsync(m => m.MensajeId == mensajeId, ct);
                        }
                    }
                }

                // Mismo folio (y error, si lo hubo) que la primera vez; nada nuevo se crea.
                return Results.Json(new Acuse(mensajeId, "DUPLICADO", previo.Folio, previo.Error));
            }
        })
        .AllowAnonymous()
        .WithTags("Integraciones Almacenes")
        .WithName("RecibirMensajeAlmacenesApp")
        .Produces<Acuse>(StatusCodes.Status200OK)
        .Produces<Acuse>(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // Bandeja de revisión (J6) y de excepciones (A4.3-a): salidas aceptadas sin existencia suficiente
        // (EN_REVISION) y salidas rechazadas por datos (ERROR). Ninguna movió saldo.
        // A4.8: también los consumos de componentes por composición sin descontar (EN_REVISION: sin norma, factor,
        // sucursal, artículo o existencia; EXCEPCION: pieza sin componentes legibles, solo se descontó el vidrio).
        app.MapGet("/api/v1/almacen/mensajes-app/revision", async (AlmacenDbContext db, CancellationToken ct) =>
        {
            var mensajes = await db.MensajesApp.AsNoTracking()
                .Where(m => m.Estado == Millet.Almacen.Application.AppAlmacenes.SalidaAppAlmacenes.EnRevision
                    || (m.Estado == "ERROR" && TiposSalida.Contains(m.Tipo)))
                .OrderByDescending(m => m.RecibidoAt)
                .Select(m => new FilaBandeja(m.MensajeId, m.Tipo, m.Estado, m.Referencia, m.Error, m.RecibidoAt, m.Payload))
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
