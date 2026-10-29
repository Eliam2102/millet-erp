using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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

    public sealed record Acuse(string? MensajeId, string Estado, string? Folio, string? Error);

    public static IEndpointRouteBuilder MapAlmacenesAppMensajesEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/integraciones/almacenes/mensajes", async (
            HttpRequest request, IConfiguration configuration, AlmacenDbContext db,
            Millet.Almacen.Domain.Ports.IComprasCrearRqAppPort rqApp, CancellationToken ct) =>
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
                if (previo is null)
                {
                    // Punto de enchufe de handlers: un case por tipo.
                    var (estado, folio, error) = tipo switch
                    {
                        "REQUISICION_ENVIADA" => await AlmacenesAppRequisicionEnviada.ManejarAsync(datos, rqApp, ct),
                        _ => ("ERROR", (string?)null, (string?)"tipo aún no soportado"),
                    };
                    // Un ERROR de datos de la RQ (proveedor/artículo sin dar de alta) se corrige en el ERP
                    // y la app reenvía el mismo mensajeId: no se guarda para que el reenvío se procese.
                    if (tipo == "REQUISICION_ENVIADA" && estado == "ERROR")
                        return Results.Json(new Acuse(mensajeId, estado, folio, error));
                    var referencia = Str("referencia");
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

        return app;
    }
}
