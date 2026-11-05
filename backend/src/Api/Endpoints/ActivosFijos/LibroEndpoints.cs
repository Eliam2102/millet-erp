using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Api.Endpoints.ActivosFijos;
public static class LibroEndpoints
{
    public static IEndpointRouteBuilder MapLibroActivos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/activos-fijos").WithTags("Libro de activos").RequireAuthorization();
        g.MapGet("/depreciacion/previa", async (DateOnly periodo, LibroServicio libro, CancellationToken ct) => Results.Ok(await libro.PreviaAsync(periodo, true, ct)))
            .RequireAuthorization(P("depreciacion.correr"));
        g.MapPost("/depreciacion", async (PeriodoBody datos, LibroServicio libro, CancellationToken ct) => Results.Ok(await libro.ConfirmarAsync(datos.Periodo, ct)))
            .RequireAuthorization(P("depreciacion.correr")).WithMetadata(new RequireIdempotencyKeyAttribute());
        g.MapPost("/depreciacion/{id:guid}/anular", async (Guid id, MotivoBody datos, HttpRequest req, LibroServicio libro, CancellationToken ct) =>
            Results.Ok(await libro.AnularAsync(id, Version(req), datos.Motivo, ct)))
            .RequireAuthorization(P("depreciacion.anular")).WithMetadata(new RequireIdempotencyKeyAttribute()).AddEndpointFilter(ExigirVersion);
        g.MapGet("/depreciacion", async (ActivosFijosDbContext db, ActivoServicio acceso, LibroServicio libro, CancellationToken ct) =>
        {
            var ids = await acceso.SucursalesAsync(null, ct);
            var corridas = await db.Set<CorridaDepreciacion>().AsNoTracking().Include(x => x.Detalles.Where(d => ids == null || ids.Contains(d.SucursalId)))
                .Where(x => ids == null || x.Detalles.Any(d => ids.Contains(d.SucursalId))).OrderByDescending(x => x.Periodo).ToListAsync(ct);
            await libro.MarcarEfectoContableAsync(corridas,ct); return Results.Ok(corridas);
        }).RequireAuthorization(P("activos.ver"));
        g.MapPost("/depreciacion/{id:guid}/recuperar-poliza", async (Guid id, HttpRequest req, LibroServicio libro, CancellationToken ct) => Results.Ok(await libro.RecuperarPolizaAsync(id,Version(req),ct)))
            .RequireAuthorization(P("depreciacion.correr")).WithMetadata(new RequireIdempotencyKeyAttribute()).AddEndpointFilter(ExigirVersion);
        g.MapGet("/activos/{id:guid}/libro", async (Guid id, ActivosFijosDbContext db, ActivoServicio acceso, Millet.Facturacion.Domain.Ports.IDisposicionVentaReadPort ventas, CancellationToken ct) =>
        {
            var a = await acceso.CargarAsync(id, false, ct);
            return Results.Ok(new { Ventas = await ventas.HistorialAsync(a.Numero, ct), Depreciaciones = await db.Set<CorridaDepreciacion>().AsNoTracking().Include(c => c.Detalles.Where(d => d.ActivoId == id)).Where(c => c.Detalles.Any(d => d.ActivoId == id)).ToListAsync(ct),
                Operaciones = await db.Set<OperacionLibro>().AsNoTracking().Where(o => o.ActivoId == id).ToListAsync(ct) });
        }).RequireAuthorization(P("activos.ver"));
        g.MapPost("/activos/{id:guid}/operaciones", async (Guid id, SolicitudOperacion datos, HttpRequest req, OperacionesLibroServicio servicio, CancellationToken ct) => Results.Ok(await servicio.RegistrarAsync(id, Version(req), datos, ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute()).AddEndpointFilter(ExigirVersion); // El servicio selecciona el permiso según el tipo.
        g.MapPost("/operaciones/{id:guid}/autorizar", async (Guid id, HttpRequest req, OperacionesLibroServicio servicio, CancellationToken ct) => Results.Ok(await servicio.AutorizarAsync(id, Version(req), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute()).AddEndpointFilter(ExigirVersion);
        g.MapPost("/operaciones/{id:guid}/rechazar", async (Guid id, MotivoBody datos, HttpRequest req, OperacionesLibroServicio servicio, CancellationToken ct) => Results.Ok(await servicio.RechazarAsync(id, Version(req), datos.Motivo, ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute()).AddEndpointFilter(ExigirVersion);
        g.MapGet("/autorizaciones", async (ActivosFijosDbContext db, ActivoServicio acceso, CancellationToken ct) =>
        {
            var ids = await acceso.SucursalesAsync(null, ct);
            return Results.Ok(await (from o in db.Set<OperacionLibro>().AsNoTracking() join a in db.Activos on o.ActivoId equals a.Id
                where !o.Anulada && o.PolizaId == null && (ids == null || ids.Contains(a.SucursalId))
                orderby o.CreatedAt select new { Operacion = o, a.Numero, a.Descripcion }).ToListAsync(ct));
        }).RequireAuthorization(P("activos.ver"));
        g.MapGet("/carga/plantilla", () => Results.File(System.Text.Encoding.UTF8.GetBytes(CargaLibroServicio.Plantilla), "text/csv", "libro-sap.csv")).RequireAuthorization(P("carga.ejecutar"));
        g.MapGet("/carga/plantilla.xlsx", () => Results.File(ArchivoLibro.PlantillaExcel(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "libro-sap.xlsx")).RequireAuthorization(P("carga.ejecutar"));
        g.MapPost("/carga/previa", async (HttpRequest req, CargaLibroServicio servicio, CancellationToken ct) => Results.Ok(await Cargar(req, servicio, false, ct))).RequireAuthorization(P("carga.ejecutar"));
        g.MapPost("/carga/confirmar", async (HttpRequest req, CargaLibroServicio servicio, CancellationToken ct) => Results.Ok(await Cargar(req, servicio, true, ct)))
            .RequireAuthorization(P("carga.ejecutar")).WithMetadata(new RequireIdempotencyKeyAttribute());
        g.MapGet("/carga", async (ActivosFijosDbContext db, ActivoServicio acceso, LibroServicio libro, CancellationToken ct) =>
        {
            await acceso.SucursalesAsync(null,ct);
            var cargas = await db.Set<CargaLibro>().AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
            foreach (var carga in cargas)
                foreach (var sucursal in await db.Activos.IgnoreQueryFilters().Where(a => a.EmpresaId == libro.Empresa && a.DocumentoSoporte == $"SAP:{carga.Id}").Select(a => a.SucursalId).Distinct().ToListAsync(ct)) await acceso.SucursalAsync(sucursal,false,ct);
            return Results.Ok(cargas);
        }).RequireAuthorization(P("carga.ejecutar"));
        g.MapPost("/carga/{id:guid}/anular", async (Guid id,MotivoBody datos,HttpRequest req,CargaLibroServicio servicio,CancellationToken ct) => Results.Ok(await servicio.AnularAsync(id,Version(req),datos.Motivo,ct)))
            .RequireAuthorization(P("carga.ejecutar")).WithMetadata(new RequireIdempotencyKeyAttribute()).AddEndpointFilter(ExigirVersion);
        g.MapPost("/conciliacion", async (RegistrarConciliacionBody datos, ReportesLibroServicio servicio,CancellationToken ct) => Results.Ok(await servicio.RegistrarAsync(datos.Periodo,datos.SucursalId,datos.Diferencias,ct)))
            .RequireAuthorization(P("conciliacion.ver")).WithMetadata(new RequireIdempotencyKeyAttribute());
        g.MapGet("/conciliacion/historial", async (DateOnly periodo,Guid? sucursalId,ReportesLibroServicio servicio,CancellationToken ct) => Results.Ok(await servicio.HistorialAsync(periodo,sucursalId,ct))).RequireAuthorization(P("conciliacion.ver"));
        g.MapPut("/conciliacion/{id:guid}", () => Results.Problem(statusCode:422,title:"Una conciliación aceptada no se edita; registre otra conciliación del periodo.")).RequireAuthorization(P("conciliacion.ver"));
        g.MapGet("/conciliacion", async (DateOnly periodo, Guid? sucursalId, ReportesLibroServicio servicio, CancellationToken ct) => Results.Ok(await servicio.ConciliarAsync(periodo, sucursalId, ct))).RequireAuthorization(P("conciliacion.ver"));
        g.MapGet("/reportes/libro", async (DateOnly corte, Guid? sucursalId, ReportesLibroServicio servicio, CancellationToken ct) => Results.Ok(await servicio.LibroAsync(corte, sucursalId, ct))).RequireAuthorization(P("activos.ver"));
        g.MapGet("/configuracion-libro", async (ActivosFijosDbContext db, CancellationToken ct) => Results.Ok(await db.Set<ConfiguracionLibro>().AsNoTracking().SingleOrDefaultAsync(ct))).RequireAuthorization(P("clases.administrar"));
        g.MapPut("/configuracion-libro", async (ConfiguracionBody datos, HttpRequest req, ActivosFijosDbContext db, LibroServicio libro, ICuentaContableReadPort cuentas, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct); await libro.BloquearAsync(ct);
            var config = await db.Set<ConfiguracionLibro>().SingleOrDefaultAsync(ct);
            if (config != null && config.Version != Version(req)) throw new ConcurrencyException("ConfiguracionLibro", config.Id);
            foreach (var cuenta in new[] { datos.CuentaUtilidadId, datos.CuentaPerdidaId, datos.CuentaVentaPuenteId, datos.CuentaRevalorizacionId }.OfType<Guid>())
                if (!(await cuentas.ValidarParaMovimientoAsync(cuenta, OrigenMovimiento.AuxiliarCxP, ct)).Valida) throw new BusinessRuleException("AF_CUENTA_INVALIDA", "La cuenta no admite movimientos.");
            if (config == null) { config = new() { EmpresaId = libro.Empresa }; db.Add(config); }
            config.DepreciarMismoMes = datos.DepreciarMismoMes; config.InicioDepreciacionConfirmado = datos.InicioDepreciacionConfirmado;
            config.RevalorizacionHabilitada = datos.RevalorizacionHabilitada; config.CuentaUtilidadId = datos.CuentaUtilidadId; config.CuentaPerdidaId = datos.CuentaPerdidaId;
            config.CuentaVentaPuenteId = datos.CuentaVentaPuenteId; config.CuentaRevalorizacionId = datos.CuentaRevalorizacionId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Ok(config);
        }).RequireAuthorization(P("clases.administrar")).WithMetadata(new RequireIdempotencyKeyAttribute());
        g.MapGet("/activos-mejorables", async (string? q, Guid? seleccionado, ActivosFijosDbContext db, ActivoServicio acceso, CancellationToken ct) =>
        {
            var ids = await acceso.SucursalesAsync(null, ct);
            var consulta = from a in db.Activos.AsNoTracking() join c in db.Clases on a.ClaseId equals c.Id
                where a.Estado == EstadoActivo.Activo && c.Clave != "170" && (ids == null || ids.Contains(a.SucursalId))
                && (q == null || a.Numero.Contains(q) || a.Descripcion.Contains(q) || a.Id == seleccionado)
                orderby a.Id == seleccionado descending, a.Numero
                select new { a.Id, a.Numero, a.Descripcion };
            return Results.Ok(await consulta.Take(100).ToListAsync(ct));
        }).RequireAuthorization(P("activos.gestionar"));
        g.MapGet("/reportes/depreciacion-fiscal", async (int ejercicio, int mesCorte, FiscalServicio fiscal, CancellationToken ct) =>
            Results.Ok(await fiscal.ReporteAsync(ejercicio, mesCorte, ct))).RequireAuthorization(P("activos.ver"));
        g.MapGet("/inpc", async (FiscalServicio fiscal, CancellationToken ct) => Results.Ok(await fiscal.ListarInpcAsync(ct))).RequireAuthorization(P("clases.administrar"));
        g.MapPost("/inpc", async (CapturaInpc datos, FiscalServicio fiscal, CancellationToken ct) =>
        { await fiscal.GuardarInpcAsync([datos], ct); return Results.NoContent(); })
            .RequireAuthorization(P("clases.administrar")).WithMetadata(new RequireIdempotencyKeyAttribute());
        g.MapPost("/inpc/excel", async (HttpRequest req, FiscalServicio fiscal, CancellationToken ct) =>
        {
            if (req.ContentLength > ArchivoLibro.LimiteBytes + 1024 * 1024) throw new BadHttpRequestException("Máximo 10 MB.", 413);
            if (!req.HasFormContentType) throw new BusinessRuleException("AF_INPC_ARCHIVO_INVALIDO", "Seleccione un Excel en el campo archivo.");
            var form = await req.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = ArchivoLibro.LimiteBytes }, ct);
            var archivo = form.Files.GetFile("archivo");
            if (form.Files.Count != 1 || archivo == null || archivo.Length > ArchivoLibro.LimiteBytes || !archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("AF_INPC_ARCHIVO_INVALIDO", "Seleccione un único archivo .xlsx de hasta 10 MB.");
            using var stream = new MemoryStream(); await archivo.CopyToAsync(stream, ct);
            await fiscal.GuardarInpcAsync(FiscalServicio.LeerExcel(stream.ToArray()), ct); return Results.NoContent();
        }).RequireAuthorization(P("clases.administrar")).WithMetadata(new RequireIdempotencyKeyAttribute());
        return app;
    }
    public sealed record RegistrarConciliacionBody(DateOnly Periodo, Guid? SucursalId, IReadOnlyList<ClasificacionDiferencia> Diferencias);
    private static async Task<ResultadoCarga> Cargar(HttpRequest req, CargaLibroServicio servicio, bool confirmar, CancellationToken ct)
    {
        if (req.ContentLength > ArchivoLibro.LimiteBytes + 1024 * 1024)
            throw new BadHttpRequestException("El archivo excede el límite de 10 MB.", StatusCodes.Status413PayloadTooLarge);
        if (req.HasJsonContentType())
        {
            var datos = await req.ReadFromJsonAsync<SolicitudCarga>(ct);
            if (datos == null || datos.Csv == null || datos.Totales == null)
                throw new BusinessRuleException("AF_ARCHIVO_INVALIDO", "Capture CSV, versión, corte y totales.");
            return await servicio.EjecutarAsync(datos, confirmar, ct);
        }
        if (!req.HasFormContentType || !req.ContentType!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("AF_ARCHIVO_INVALIDO", "Envíe el archivo como formulario o use el formato JSON anterior.");
        try
        {
            var form = await req.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = ArchivoLibro.LimiteBytes, ValueLengthLimit = 64 * 1024 }, ct);
            var archivo = form.Files.GetFile("archivo");
            if (form.Files.Count != 1 || archivo == null || archivo.Length == 0 || archivo.Length > ArchivoLibro.LimiteBytes)
                throw new BusinessRuleException("AF_ARCHIVO_INVALIDO", "Seleccione un archivo de hasta 10 MB en el campo archivo.");
            if (!DateOnly.TryParseExact(form["corte"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var corte))
                throw new BusinessRuleException("AF_CORTE_INVALIDO", "Capture el corte como aaaa-mm-dd.");
            var totales = JsonSerializer.Deserialize<List<TotalSap>>(form["totales"].FirstOrDefault() ?? "[]", JsonSerializerOptions.Web)
                ?? throw new JsonException();
            using var stream = new MemoryStream();
            await archivo.CopyToAsync(stream, ct);
            return await servicio.EjecutarArchivoAsync(new("", form["versionArchivo"].ToString(), corte, totales, Guid.TryParse(form["sucursalId"],out var destino) ? destino : null), stream.ToArray(), archivo.FileName, form["hoja"].FirstOrDefault(), confirmar, ct);
        }
        catch (JsonException) { throw new BusinessRuleException("AF_TOTALES_INVALIDOS", "Los totales deben ser una lista de clase, valor y acumulada."); }
        catch (InvalidDataException) { throw new BusinessRuleException("AF_ARCHIVO_INVALIDO", "Revise el formulario y el límite de 10 MB del archivo."); }
    }
    private static ValueTask<object?> ExigirVersion(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var valor = context.HttpContext.Request.Headers.IfMatch.ToString().Trim('"');
        return int.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v > 0
            ? next(context) : ValueTask.FromResult<object?>(Results.Problem(statusCode: 428, title: "Envíe If-Match con la versión vigente."));
    }
    private static string P(string p) => PermissionPolicyProvider.Prefix + "activos_fijos." + p;
    private static int Version(HttpRequest req) => int.TryParse(req.Headers.IfMatch.ToString().Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : throw new BusinessRuleException("AF_VERSION_REQUERIDA", "Envíe If-Match con la versión vigente.");
    public sealed record PeriodoBody(DateOnly Periodo);
    public sealed record MotivoBody(string Motivo);
    public sealed record ConfiguracionBody(bool RevalorizacionHabilitada, Guid? CuentaUtilidadId, Guid? CuentaPerdidaId, Guid? CuentaVentaPuenteId, Guid? CuentaRevalorizacionId, bool DepreciarMismoMes = false, bool InicioDepreciacionConfirmado = false);
}
