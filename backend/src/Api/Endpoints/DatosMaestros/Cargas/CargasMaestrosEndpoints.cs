using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.DatosMaestros.Application.Cargas;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.DatosMaestros.Cargas;

public static class CargasMaestrosEndpoints
{
    public static IEndpointRouteBuilder MapCargasMaestrosEndpoints(this IEndpointRouteBuilder app)
    {
        (string Catalogo, string Permiso)[] catalogos = [
            ("clientes", PermisosCanonicos.DatosMaestrosClientesCargar), ("articulos", PermisosCanonicos.DatosMaestrosArticulosCargar),
            ("proveedores", PermisosCanonicos.DatosMaestrosProveedoresCargar), ("productos-aw-fiscal", PermisosCanonicos.DatosMaestrosProductosAwFiscalCargar),
            ("centros-costo-dim1", PermisosCanonicos.CentrosCostoCatalogoCargar), ("centros-costo-dim2", PermisosCanonicos.CentrosCostoCatalogoCargar), ("centros-costo-dim3", PermisosCanonicos.CentrosCostoCatalogoCargar)];
        foreach (var (catalogo, permiso) in catalogos)
        {
            var grupo = app.MapGroup("/api/v1/datos-maestros/cargas/" + catalogo).WithTags("Carga de datos maestros")
                .RequireAuthorization(PermissionPolicyProvider.Prefix + permiso)
                .WithMetadata(new RequestSizeLimitAttribute(ArchivoMaestros.LimiteBytes + 64 * 1024));
            foreach (var formato in new[] { "xlsx", "csv" })
            {
                var excel = formato == "xlsx";
                grupo.MapGet("/plantilla." + formato, (CargaMaestrosServicio servicio) => Results.File(
                    ArchivoMaestros.Plantilla(servicio.Adaptador(catalogo).Columnas, excel),
                    excel ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "text/csv; charset=utf-8", $"plantilla-{catalogo}.{formato}"));
            }
            grupo.MapPost("/previa", async (HttpRequest request, CargaMaestrosServicio servicio, CancellationToken ct) =>
            {
                var (archivo, contenido, _) = await LeerAsync(request, ct);
                return Results.Ok(await servicio.EjecutarAsync(catalogo, contenido, archivo.FileName, false, null, ct));
            }).DisableAntiforgery();
            grupo.MapPost("/confirmar", async (HttpRequest request, CargaMaestrosServicio servicio, CancellationToken ct) =>
            {
                var (archivo, contenido, huella) = await LeerAsync(request, ct);
                return Results.Ok(await servicio.EjecutarAsync(catalogo, contenido, archivo.FileName, true, huella, ct));
            }).DisableAntiforgery().WithMetadata(new RequireIdempotencyKeyAttribute());
        }
        return app;
    }
    private static async Task<(IFormFile Archivo, byte[] Contenido, string Huella)> LeerAsync(HttpRequest request, CancellationToken ct)
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
        return (archivo, contenido.ToArray(), form["huella"].ToString());
    }
}
