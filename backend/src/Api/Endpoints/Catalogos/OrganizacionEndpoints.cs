using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.Auth;
using Millet.Identidad.Domain;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Catalogos;

/// <summary>
/// Endpoints HTTP read-only de catálogos organizacionales en el schema
/// <c>compartido</c> (B.1, brief de UI Compras):
/// <list type="bullet">
///   <item><c>GET /api/v1/catalogos/sucursales</c> — list paginado con filtros.</item>
///   <item><c>GET /api/v1/catalogos/departamentos</c> — list paginado con filtros.</item>
///   <item><c>GET /api/v1/catalogos/almacenes</c> — list paginado con filtro extra <c>sucursalId</c>.</item>
///   <item><c>GET /api/v1/catalogos/puestos</c> — list paginado (ADM-PR1).</item>
///   <item><c>GET /api/v1/catalogos/empleados</c> — list paginado con filtros extra (ADM-PR1).</item>
/// </list>
///
/// <para>
/// Permiso: <c>compartido.catalogos.leer</c> (mismo que proveedores y
/// artículos). Read-only en MVP. CRUD entra en v1.1.
/// </para>
/// </summary>
public static class OrganizacionEndpoints
{
    private const int LimitMax = 200;

    public static IEndpointRouteBuilder MapOrganizacionEndpoints(this IEndpointRouteBuilder app)
    {
        var sucursales = app.MapGroup("/api/v1/catalogos/sucursales").WithTags("Catalogos");
        var deptos = app.MapGroup("/api/v1/catalogos/departamentos").WithTags("Catalogos");
        var almacenes = app.MapGroup("/api/v1/catalogos/almacenes").WithTags("Catalogos");
        var puestos = app.MapGroup("/api/v1/catalogos/puestos").WithTags("Catalogos");
        var empleados = app.MapGroup("/api/v1/catalogos/empleados").WithTags("Catalogos");

        // --- Sucursales ---
        sucursales.MapGet("/", async (
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] string? q,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            CompartidoDbContext db,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            IQueryable<Sucursal> query = db.Sucursales.AsNoTracking();
            if (estatus is EstatusCatalogo e) query = query.Where(s => s.Estatus == e);
            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(s => s.Clave.Contains(q) || s.Nombre.Contains(q));
            }

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderBy(s => s.Clave)
                .Skip(off).Take(lim)
                .Select(s => new SucursalListItem(s.Id, s.Clave, s.Nombre, s.Estatus))
                .ToListAsync(ct);

            return Results.Ok(new PagedCatalogoResponse<SucursalListItem>(items, off, lim, total));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarSucursales")
        .WithSummary("Listar sucursales (B.1)")
        .WithDescription(
            "Catálogo organizacional. Filtros opcionales: `estatus` " +
            "(0=Activo, 1=Inactivo, 2=EnRevisión), `q` (substring " +
            "contra `clave` o `nombre`). Paginación offset-based, " +
            $"tope `limit={LimitMax}`. Permiso: " +
            "`compartido.catalogos.leer`.")
        .Produces<PagedCatalogoResponse<SucursalListItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- Departamentos ---
        deptos.MapGet("/", async (
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] string? q,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            CompartidoDbContext db,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            IQueryable<Departamento> query = db.Departamentos.AsNoTracking();
            if (estatus is EstatusCatalogo e) query = query.Where(d => d.Estatus == e);
            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(d => d.Clave.Contains(q) || d.Nombre.Contains(q));
            }

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderBy(d => d.Clave)
                .Skip(off).Take(lim)
                .Select(d => new DepartamentoListItem(d.Id, d.Clave, d.Nombre, d.Estatus))
                .ToListAsync(ct);

            return Results.Ok(new PagedCatalogoResponse<DepartamentoListItem>(items, off, lim, total));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarDepartamentos")
        .WithSummary("Listar departamentos (B.1)")
        .WithDescription(
            "Catálogo funcional (Compras, Almacén, Mantenimiento, etc). " +
            "Filtros: `estatus`, `q`. Paginado.")
        .Produces<PagedCatalogoResponse<DepartamentoListItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- Almacenes ---
        // F1-PR2: re-apuntado al nuevo schema `almacen.almacenes` vía AlmacenDbContext.
        // El placeholder `compartido.almacenes` queda DROPPED en esta migración.
        almacenes.MapGet("/", async (
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] Guid? sucursalId,
            [FromQuery] string? q,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            AlmacenDbContext db,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            IQueryable<Millet.Almacen.Domain.Catalogo.Almacen> query = db.Almacenes.AsNoTracking();
            if (estatus is EstatusCatalogo e) query = query.Where(a => a.Estatus == e);
            if (sucursalId is Guid sid) query = query.Where(a => a.SucursalId == sid);
            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(a => a.Clave.Contains(q) || a.Nombre.Contains(q));
            }

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderBy(a => a.Clave)
                .Skip(off).Take(lim)
                .Select(a => new AlmacenListItem(a.Id, a.Clave, a.Nombre, a.SucursalId, a.Estatus))
                .ToListAsync(ct);

            return Results.Ok(new PagedCatalogoResponse<AlmacenListItem>(items, off, lim, total));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarAlmacenes")
        .WithSummary("Listar almacenes (B.1)")
        .WithDescription(
            "Cada almacén pertenece a una sucursal (FK física). Filtros: " +
            "`estatus`, `sucursalId` (común para poblar dropdown " +
            "dependiente \"primero sucursal, después almacén\"), `q`.")
        .Produces<PagedCatalogoResponse<AlmacenListItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- Puestos (ADM-PR1, doc 10-catalogo-puestos-empleados) ---
        puestos.MapGet("/", async (
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] string? q,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            CompartidoDbContext db,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            IQueryable<Puesto> query = db.Puestos.AsNoTracking();
            if (estatus is EstatusCatalogo e) query = query.Where(p => p.Estatus == e);
            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(p => p.Clave.Contains(q) || p.Nombre.Contains(q));
            }

            var total = await query.CountAsync(ct);
            var deptos = await db.Departamentos.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Nombre, ct);
            var itemsRaw = await query
                .OrderBy(p => p.Clave)
                .Skip(off).Take(lim)
                .ToListAsync(ct);
            var items = itemsRaw
                .Select(p => new PuestoListItem(
                    p.Id,
                    p.Clave,
                    p.Nombre,
                    p.Estatus,
                    p.DepartamentoId,
                    p.DepartamentoId != null && deptos.TryGetValue(p.DepartamentoId.Value, out var dn) ? dn : null,
                    p.RolSugeridoId))
                .ToList();

            return Results.Ok(new PagedCatalogoResponse<PuestoListItem>(items, off, lim, total));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarPuestos")
        .WithSummary("Listar puestos (ADM-PR1)")
        .WithDescription(
            "Catálogo de puestos organizacionales — llave de las políticas " +
            "de viáticos por puesto. Filtros: `estatus`, `q`. Paginado.")
        .Produces<PagedCatalogoResponse<PuestoListItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- Empleados (ADM-PR1) ---
        empleados.MapGet("/", async (
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] Guid? empresaId,
            [FromQuery] Guid? puestoId,
            [FromQuery] Guid? sucursalId,
            [FromQuery] string? q,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            CompartidoDbContext db,
            IdentidadDbContext identidadDb,
            ICurrentUserContext currentUser,
            ICurrentUserPermissions permisos,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            IQueryable<Empleado> query = db.Empleados.AsNoTracking();
            // El catálogo contiene correo y vínculo de acceso: una persona
            // operativa nunca debe listar empleados de sucursales ajenas.
            if (!await permisos.TieneAsync(PermisosCanonicos.AdminEmpleadosLeerTodasSucursales, ct))
            {
                var sucursalesPermitidas = currentUser.UserId is Guid uid
                    ? await identidadDb.UsuarioSucursales.AsNoTracking()
                        .Where(a => a.UsuarioId == uid && a.Estatus == EstatusCatalogo.Activo)
                        .Select(a => a.SucursalId).ToListAsync(ct)
                    : [];
                if (sucursalId is Guid solicitada && !sucursalesPermitidas.Contains(solicitada))
                    throw new ForbiddenException("SUCURSAL_NO_ASOCIADA",
                        "No tienes acceso a los empleados de esta sucursal.");
                query = query.Where(x => x.SucursalId.HasValue &&
                    sucursalesPermitidas.Contains(x.SucursalId.Value));
            }
            if (estatus is EstatusCatalogo e) query = query.Where(x => x.Estatus == e);
            if (empresaId is Guid eid) query = query.Where(x => x.EmpresaId == eid);
            if (puestoId is Guid pid) query = query.Where(x => x.PuestoId == pid);
            if (sucursalId is Guid sid) query = query.Where(x => x.SucursalId == sid);
            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(x =>
                    x.Clave.Contains(q) || x.Nombre.Contains(q)
                    || (x.Email != null && x.Email.Contains(q)));
            }

            var total = await query.CountAsync(ct);
            var puestosDict = await db.Puestos.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);
            var deptosDict = await db.Departamentos.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Nombre, ct);
            var itemsRaw = await query
                .OrderBy(x => x.Nombre)
                .Skip(off).Take(lim)
                .ToListAsync(ct);
            var items = itemsRaw
                .Select(x => new EmpleadoListItem(
                    x.Id, x.EmpresaId, x.Clave, x.Nombre, x.Email,
                    x.PuestoId, x.JefeDirectoId, x.SucursalId,
                    x.DepartamentoId, x.UsuarioId, x.Estatus,
                    x.PuestoId != null && puestosDict.TryGetValue(x.PuestoId.Value, out var pn) ? pn : null,
                    x.DepartamentoId != null && deptosDict.TryGetValue(x.DepartamentoId.Value, out var dn) ? dn : null))
                .ToList();

            return Results.Ok(new PagedCatalogoResponse<EmpleadoListItem>(items, off, lim, total));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarEmpleados")
        .WithSummary("Listar empleados (ADM-PR1)")
        .WithDescription(
            "Master de empleados para reglas de negocio por persona. El " +
            "list item incluye `puestoId` y `jefeDirectoId` para que la " +
            "solicitud de viáticos prellene tope (política) y autorizador " +
            "N1 sin fetch extra. Filtros: `estatus`, `empresaId`, " +
            "`puestoId`, `sucursalId`, `q` (clave, nombre o email). Paginado.")
        .Produces<PagedCatalogoResponse<EmpleadoListItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static (int off, int lim) NormalizePaging(int? offset, int? limit)
    {
        var off = offset is < 0 ? 0 : offset ?? 0;
        var lim = limit switch
        {
            null or <= 0 => 50,
            > 200 => 200,
            _ => limit.Value,
        };
        return (off, lim);
    }
}

public sealed record SucursalListItem(
    Guid Id,
    string Clave,
    string Nombre,
    EstatusCatalogo Estatus);

public sealed record DepartamentoListItem(
    Guid Id,
    string Clave,
    string Nombre,
    EstatusCatalogo Estatus);

public sealed record AlmacenListItem(
    Guid Id,
    string Clave,
    string Nombre,
    Guid SucursalId,
    EstatusCatalogo Estatus);

public sealed record PuestoListItem(
    Guid Id,
    string Clave,
    string Nombre,
    EstatusCatalogo Estatus,
    Guid? DepartamentoId = null,
    string? DepartamentoNombre = null,
    Guid? RolSugeridoId = null);

public sealed record EmpleadoListItem(
    Guid Id,
    Guid EmpresaId,
    string Clave,
    string Nombre,
    string? Email,
    Guid? PuestoId,
    Guid? JefeDirectoId,
    Guid? SucursalId,
    Guid? DepartamentoId,
    Guid? UsuarioId,
    EstatusCatalogo Estatus,
    string? PuestoNombre = null,
    string? DepartamentoNombre = null);
