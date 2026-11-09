using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Almacen.Application.Roturas;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Roturas;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// Salida por rotura en almacén (ALM-03), en dos pasos: capturar con <c>almacen.salidas.rotura-capturar</c> (sin efecto en el
/// saldo) y autorizar o rechazar con <c>almacen.salidas.rotura-autorizar</c> (otro usuario). Lectura con el permiso de la
/// bandeja de salidas. Alcance por sucursal (ADR-0051).
/// </summary>
public static class SalidasRoturaEndpoints
{
    public sealed record SalidaRoturaResponse(Guid Id, Guid SucursalId, Guid UbicacionId, string? UbicacionClave, Guid ArticuloId,
        string? ArticuloClave, string? ArticuloDescripcion, decimal Cantidad, string UnidadMedida, DateOnly Fecha, string MotivoCodigo,
        string Motivo, string? Comentario, string Estado, string CapturadoPor, DateTimeOffset CapturadoAt, string? DecididoPor,
        DateTimeOffset? DecididoAt, string? MotivoRechazo, Guid? MovimientoId, string? FolioMovimiento, decimal? CostoUnitarioMxn,
        decimal? ValorMxn, string? AreaClave = null, string? EquipoClave = null);

    public sealed record MotivoRoturaResponse(string Codigo, string Texto);
    public sealed record CapturarRoturaRequest(Guid UbicacionId, Guid ArticuloId, decimal Cantidad, DateOnly? Fecha, string MotivoCodigo, string? Comentario,
        string? AreaClave = null, string? EquipoClave = null);

    /// <summary>86e3zf04y: si la cuenta de merma exige área o equipo (regla C-03) para esa ubicación y fecha.</summary>
    public sealed record DimensionesRoturaResponse(bool ExigeArea, bool ExigeEquipo, string? CuentaCodigo);

    /// <summary>La rotura de la pantalla carga la plantilla de su propio evento (misma póliza que la rotura de la app).</summary>
    private const string EventoRotura = "almacen.salida_rotura.registrada.v1";
    private const string CausaRotura = "rotura";
    public sealed record RechazarRoturaRequest(string Motivo);

    public static IEndpointRouteBuilder MapSalidasRoturaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/almacen/salidas-rotura").WithTags("Almacen");

        group.MapGet("/motivos", async (ICurrentUserPermissions permisos, CancellationToken ct) =>
        {
            await ExigirOpcionRoturaAsync(permisos, ct);
            return Results.Ok(SalidaRotura.Motivos.Select(m => new MotivoRoturaResponse(m.Key, m.Value)).ToList());
        })
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasLeerTodas)
            .WithName("ListarMotivosRotura")
            .Produces<List<MotivoRoturaResponse>>(StatusCodes.Status200OK);

        // 86e3zf04y: la pantalla pide área y equipo solo cuando la cuenta de merma los exige (C-03), igual que H1 en la app.
        group.MapGet("/dimensiones", async ([FromQuery] Guid ubicacionId, [FromQuery] DateOnly? fecha, AlmacenDbContext db,
            ICurrentUserPermissions permisos, ICurrentEmpresaContext empresa, IDimensionesSalidaInventarioPort dimensiones, CancellationToken ct) =>
        {
            await ExigirOpcionRoturaAsync(permisos, ct);
            var sucursalId = await SucursalDeUbicacionAsync(db, ubicacionId, ct);
            var d = await dimensiones.ResolverAsync(new SolicitudDimensionesSalida(
                empresa.Current ?? throw new BusinessRuleException("ROTURA_SIN_EMPRESA", "El contexto de empresa es requerido."),
                sucursalId, CausaRotura, fecha ?? DateOnly.FromDateTime(DateTime.Now), null, null, EventoRotura), ct);
            return Results.Ok(new DimensionesRoturaResponse(d.ExigeArea, d.ExigeEquipo, d.CuentaCodigo));
        })
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasLeerTodas)
            .WithName("DimensionesSalidaRotura")
            .Produces<DimensionesRoturaResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", async ([FromQuery] string? estado, AlmacenDbContext db, DocumentoSucursalScope scope, ICurrentUserContext user,
            ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales, RoturaLectura lectura, CancellationToken ct) =>
        {
            await ExigirOpcionRoturaAsync(permisos, ct);
            var q = db.SalidasRotura.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(estado)) q = q.Where(r => r.Estado == estado);
            if (!await permisos.TieneAsync(PermisosCanonicos.AlmacenSalidasLeerTodasSucursales, ct))
            {
                var propias = user.UserId is { } uid ? await sucursales.ListarIdsAsync(uid, ct) : [];
                q = q.Where(r => propias.Contains(r.SucursalId));
            }
            var filas = await q.OrderByDescending(r => r.Fecha).ThenByDescending(r => r.CreatedAt).Take(500).ToListAsync(ct);
            return Results.Ok(await lectura.RespuestasAsync(filas, ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasLeerTodas)
        .WithName("ListarSalidasRotura")
        .Produces<List<SalidaRoturaResponse>>(StatusCodes.Status200OK);

        group.MapPost("/", async (CapturarRoturaRequest req, AlmacenDbContext db, IPeriodoContableReadPort periodo,
            IDecimalesUnidadGuard decimales, IArticuloReadPort articulos, DocumentoSucursalScope scope, ICurrentUserContext user,
            ICurrentEmpresaContext empresa, IDimensionesSalidaInventarioPort dimensiones, RoturaLectura lectura, CancellationToken ct) =>
        {
            var r = await SalidaRoturaApp.CapturarAsync(db, periodo, decimales, articulos,
                empresa.Current ?? throw new BusinessRuleException("ROTURA_SIN_EMPRESA", "El contexto de empresa es requerido."),
                user.UserId ?? Guid.Empty,
                new CapturarRoturaApp(req.UbicacionId, req.ArticuloId, req.Cantidad, req.Fecha ?? DateOnly.FromDateTime(DateTime.Now), req.MotivoCodigo, req.Comentario), ct);
            await scope.VerificarSucursalAsync(r.SucursalId, PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales, ct);
            // 86e3zf04y: área y equipo se resuelven en la sucursal con el puerto de H1. Una clave inválida no se acepta.
            // M-450 (86e3zr7ft): si la cuenta de merma los exige (regla C-03) y no vienen, la rotura tampoco se captura.
            var d = await dimensiones.ResolverAsync(new SolicitudDimensionesSalida(r.EmpresaId, r.SucursalId, CausaRotura, r.Fecha,
                req.AreaClave, req.EquipoClave, EventoRotura), ct);
            if (d.Error is not null) throw new BusinessRuleException("ROTURA_AREA_EQUIPO_INVALIDO", d.Error);
            if (d.Faltante is not null) throw new BusinessRuleException("ROTURA_AREA_EQUIPO_FALTANTE", d.Faltante);
            if (!string.IsNullOrWhiteSpace(req.AreaClave) || !string.IsNullOrWhiteSpace(req.EquipoClave))
                r.AsignarAreaEquipo(req.AreaClave, req.EquipoClave, d.CentroCostoId);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/almacen/salidas-rotura/{r.Id}", (await lectura.RespuestasAsync([r], ct))[0]);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasRoturaCapturar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CapturarSalidaRotura")
        .Produces<SalidaRoturaResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/autorizar", async (Guid id, AlmacenDbContext db, IIntegrationEventPublisher events,
            IPeriodoContableReadPort periodo, DocumentoSucursalScope scope, ICurrentUserContext user, RoturaLectura lectura, CancellationToken ct) =>
        {
            var r = await Cargar(db, id, ct);
            await scope.VerificarSucursalAsync(r.SucursalId, PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales, ct);
            await SalidaRoturaApp.AutorizarAsync(db, events, periodo, r, user.UserId ?? Guid.Empty, ct);
            return Results.Ok((await lectura.RespuestasAsync([r], ct))[0]);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasRoturaAutorizar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("AutorizarSalidaRotura")
        .Produces<SalidaRoturaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/rechazar", async (Guid id, RechazarRoturaRequest req, AlmacenDbContext db, DocumentoSucursalScope scope,
            ICurrentUserContext user, RoturaLectura lectura, CancellationToken ct) =>
        {
            var r = await Cargar(db, id, ct);
            await scope.VerificarSucursalAsync(r.SucursalId, PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales, ct);
            r.Rechazar(user.UserId ?? Guid.Empty, req.Motivo);
            await db.SaveChangesAsync(ct);
            return Results.Ok((await lectura.RespuestasAsync([r], ct))[0]);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasRoturaAutorizar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("RechazarSalidaRotura")
        .Produces<SalidaRoturaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>La opción «Salida por rotura» es solo de quien captura o autoriza; leer la bandeja de salidas no basta.</summary>
    private static async Task ExigirOpcionRoturaAsync(ICurrentUserPermissions permisos, CancellationToken ct)
    {
        if (!await permisos.TieneAsync(PermisosCanonicos.AlmacenSalidasRoturaCapturar, ct)
            && !await permisos.TieneAsync(PermisosCanonicos.AlmacenSalidasRoturaAutorizar, ct))
            throw new ForbiddenException("PERMISO_FALTANTE",
                $"Requiere el permiso '{PermisosCanonicos.AlmacenSalidasRoturaCapturar}' o '{PermisosCanonicos.AlmacenSalidasRoturaAutorizar}'.");
    }

    private static async Task<Guid> SucursalDeUbicacionAsync(AlmacenDbContext db, Guid ubicacionId, CancellationToken ct)
    {
        var subAlmacenId = await db.Ubicaciones.AsNoTracking().Where(u => u.Id == ubicacionId).Select(u => (Guid?)u.SubAlmacenId).FirstOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("ROTURA_UBICACION_INVALIDA", "La ubicación elegida no existe.");
        var (_, sucursalId) = await Millet.Almacen.Application.Catalogo.AlmacenSucursalResolver.ResolverAsync(db, subAlmacenId, ct);
        return sucursalId ?? throw new BusinessRuleException("ROTURA_SUCURSAL_REQUERIDA", "Configura la sucursal del almacén antes de registrar la rotura.");
    }

    private static async Task<SalidaRotura> Cargar(AlmacenDbContext db, Guid id, CancellationToken ct) =>
        await db.SalidasRotura.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException("ROTURA_NO_ENCONTRADA", $"No existe una salida por rotura con id '{id}'.");

    /// <summary>Arma las respuestas con clave de artículo, ubicación y nombres de usuario (una consulta por catálogo).</summary>
    public sealed class RoturaLectura(AlmacenDbContext db, IArticuloReadPort articulos, IUsuarioReadPort usuarios)
    {
        public async Task<List<SalidaRoturaResponse>> RespuestasAsync(IReadOnlyList<SalidaRotura> filas, CancellationToken ct)
        {
            var maestro = await articulos.ObtenerPorIdsAsync(filas.Select(r => r.ArticuloId).Distinct().ToList(), ct);
            var ubicIds = filas.Select(r => r.UbicacionId).Distinct().ToList();
            var ubic = await db.Ubicaciones.AsNoTracking().Where(u => ubicIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Clave, ct);
            var personas = filas.SelectMany(r => new[] { r.CapturadoPor, r.DecididoPor ?? Guid.Empty }).Where(g => g != Guid.Empty).Distinct().ToList();
            var nombres = await usuarios.ObtenerNombresAsync(personas, ct);
            string Nombre(Guid g) => nombres.GetValueOrDefault(g) ?? g.ToString();
            return filas.Select(r =>
            {
                var a = maestro.GetValueOrDefault(r.ArticuloId);
                return new SalidaRoturaResponse(r.Id, r.SucursalId, r.UbicacionId, ubic.GetValueOrDefault(r.UbicacionId), r.ArticuloId,
                    a?.Clave, a?.Descripcion, r.Cantidad, r.UnidadMedida, r.Fecha, r.MotivoCodigo, SalidaRotura.Motivos[r.MotivoCodigo],
                    r.Comentario, r.Estado, Nombre(r.CapturadoPor), r.CreatedAt, r.DecididoPor is { } d ? Nombre(d) : null, r.DecididoAt,
                    r.MotivoRechazo, r.MovimientoId, r.FolioMovimiento, r.CostoUnitarioMxn, r.ValorMxn, r.AreaClave, r.EquipoClave);
            }).ToList();
        }
    }
}
