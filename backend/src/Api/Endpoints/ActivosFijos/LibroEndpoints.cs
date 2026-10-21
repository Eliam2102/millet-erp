using System.Globalization;
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
        g.MapGet("/depreciacion", async (ActivosFijosDbContext db, ActivoServicio acceso, CancellationToken ct) =>
        {
            var ids = await acceso.SucursalesAsync(null, ct);
            return Results.Ok(await db.Set<CorridaDepreciacion>().AsNoTracking().Include(x => x.Detalles.Where(d => ids == null || ids.Contains(d.SucursalId)))
                .Where(x => ids == null || x.Detalles.Any(d => ids.Contains(d.SucursalId))).OrderByDescending(x => x.Periodo).ToListAsync(ct));
        }).RequireAuthorization(P("activos.ver"));
        g.MapGet("/activos/{id:guid}/libro", async (Guid id, ActivosFijosDbContext db, ActivoServicio acceso, CancellationToken ct) =>
        {
            await acceso.CargarAsync(id, false, ct);
            return Results.Ok(new { Depreciaciones = await db.Set<CorridaDepreciacion>().AsNoTracking().Include(c => c.Detalles.Where(d => d.ActivoId == id)).Where(c => c.Detalles.Any(d => d.ActivoId == id)).ToListAsync(ct),
                Operaciones = await db.Set<OperacionLibro>().AsNoTracking().Where(o => o.ActivoId == id).ToListAsync(ct) });
        }).RequireAuthorization(P("activos.ver"));
        g.MapPost("/activos/{id:guid}/operaciones", async (Guid id, SolicitudOperacion datos, HttpRequest req, OperacionesLibroServicio servicio, CancellationToken ct) => Results.Ok(await servicio.RegistrarAsync(id, Version(req), datos, ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute()).AddEndpointFilter(ExigirVersion); // El servicio selecciona el permiso según el tipo.
        g.MapPost("/operaciones/{id:guid}/autorizar", async (Guid id, HttpRequest req, OperacionesLibroServicio servicio, CancellationToken ct) => Results.Ok(await servicio.AutorizarAsync(id, Version(req), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute()).AddEndpointFilter(ExigirVersion);
        g.MapGet("/carga/plantilla", () => Results.File(System.Text.Encoding.UTF8.GetBytes(CargaLibroServicio.Plantilla), "text/csv", "libro-sap.csv")).RequireAuthorization(P("carga.ejecutar"));
        g.MapPost("/carga/previa", async (SolicitudCarga datos, CargaLibroServicio servicio, CancellationToken ct) => Results.Ok(await servicio.EjecutarAsync(datos, false, ct))).RequireAuthorization(P("carga.ejecutar"));
        g.MapPost("/carga/confirmar", async (SolicitudCarga datos, CargaLibroServicio servicio, CancellationToken ct) => Results.Ok(await servicio.EjecutarAsync(datos, true, ct)))
            .RequireAuthorization(P("carga.ejecutar")).WithMetadata(new RequireIdempotencyKeyAttribute());
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
        return app;
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
