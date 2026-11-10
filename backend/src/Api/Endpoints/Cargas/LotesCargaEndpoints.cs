using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Web;
using Millet.Compartido.Application.Cargas.Lotes;
using Millet.DatosMaestros.Application.Cargas;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Cargas;

/// <summary>
/// M-047 + M-167 (86e3jvvmf): lote común de cargas. Los permisos dependen del tipo de carga (cada
/// <see cref="IProcesadorCarga"/> declara el de subir y el de autorizar); el servicio los exige.
/// </summary>
public static class LotesCargaEndpoints
{
    public sealed record RechazarLoteBody(string Motivo);

    public static IEndpointRouteBuilder MapLotesCargaEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/cargas").WithTags("Lotes de carga").RequireAuthorization();

        g.MapGet("/tipos", async (LotesCargaServicio s, CancellationToken ct) => Results.Ok(await s.TiposAsync(ct)));

        g.MapPost("/tipos/{tipo}/encabezados", async (string tipo, HttpRequest request, LotesCargaServicio s, CancellationToken ct) =>
        {
            var (archivo, contenido, _) = await LeerAsync(request, ct);
            return Results.Ok(await s.EncabezadosAsync(tipo, contenido, archivo.FileName, ct));
        }).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(ArchivoMaestros.LimiteBytes + 64 * 1024));

        g.MapPost("/tipos/{tipo}/lotes", async (string tipo, HttpRequest request, LotesCargaServicio s, CancellationToken ct) =>
        {
            var (archivo, contenido, form) = await LeerAsync(request, ct);
            DateOnly? corte = null;
            var textoCorte = form["fechaCorte"].ToString();
            if (!string.IsNullOrWhiteSpace(textoCorte))
                corte = DateOnly.TryParseExact(textoCorte, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
                    ? f : throw new BusinessRuleException("LOTE_CARGA_FECHA_CORTE", "La fecha de corte debe tener el formato AAAA-MM-DD.");
            Dictionary<string, string> mapeo;
            try { mapeo = JsonSerializer.Deserialize<Dictionary<string, string>>(form["mapeo"].ToString() is { Length: > 0 } m ? m : "{}") ?? []; }
            catch (JsonException e) { throw new BusinessRuleException("LOTE_CARGA_MAPEO_INVALIDO", "El mapeo de columnas no es válido.", e); }
            Dictionary<string, string?> parametros;
            try { parametros = JsonSerializer.Deserialize<Dictionary<string, string?>>(form["parametros"].ToString() is { Length: > 0 } pj ? pj : "{}") ?? []; }
            catch (JsonException e) { throw new BusinessRuleException("LOTE_CARGA_PARAMETRO_INVALIDO", "Los parámetros de la carga no son válidos.", e); }
            return Results.Ok(await s.SubirAsync(tipo, contenido, archivo.FileName, corte, mapeo, ct, parametros));
        }).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(ArchivoMaestros.LimiteBytes + 64 * 1024))
          .WithMetadata(new RequireIdempotencyKeyAttribute());

        g.MapGet("/tipos/{tipo}/lotes", async (string tipo, LotesCargaServicio s, CancellationToken ct) => Results.Ok(await s.ListarAsync(tipo, ct)));
        g.MapGet("/lotes/{id:guid}", async (Guid id, LotesCargaServicio s, CancellationToken ct) => Results.Ok(await s.ObtenerAsync(id, ct)));
        g.MapPost("/lotes/{id:guid}/autorizar", async (Guid id, LotesCargaServicio s, CancellationToken ct) => Results.Ok(await s.AutorizarAsync(id, ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        g.MapPost("/lotes/{id:guid}/reintentar", async (Guid id, LotesCargaServicio s, CancellationToken ct) => Results.Ok(await s.ReintentarAsync(id, ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        g.MapPost("/lotes/{id:guid}/rechazar", async (Guid id, RechazarLoteBody body, LotesCargaServicio s, CancellationToken ct) =>
            Results.Ok(await s.RechazarAsync(id, body.Motivo, ct))).WithMetadata(new RequireIdempotencyKeyAttribute());
        return app;
    }

    private static async Task<(IFormFile Archivo, byte[] Contenido, IFormCollection Form)> LeerAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength > ArchivoMaestros.LimiteBytes + 64 * 1024 || !request.HasFormContentType)
            throw new BusinessRuleException("CARGA_ARCHIVO_INVALIDO", "Adjunte un archivo Excel o CSV de hasta 10 MB.");
        IFormCollection form;
        try { form = await request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = ArchivoMaestros.LimiteBytes, ValueLengthLimit = 64 * 1024, ValueCountLimit = 10 }, ct); }
        catch (InvalidDataException e) { throw new BusinessRuleException("CARGA_ARCHIVO_INVALIDO", "No se pudo leer el formulario. Adjunte un único archivo Excel o CSV de hasta 10 MB.", e); }
        var archivo = form.Files.GetFile("archivo");
        if (form.Files.Count != 1 || archivo is null || archivo.Length == 0 || archivo.Length > ArchivoMaestros.LimiteBytes)
            throw new BusinessRuleException("CARGA_ARCHIVO_INVALIDO", "Adjunte un archivo Excel o CSV de hasta 10 MB.");
        await using var contenido = new MemoryStream(); await archivo.CopyToAsync(contenido, ct);
        return (archivo, contenido.ToArray(), form);
    }
}
