using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Dimensiones;

/// <summary>
/// Literales de permisos que Contabilidad evalúa en sus handlers. Se duplican de <c>PermisosCanonicos</c> porque Contabilidad no
/// referencia Identidad (mismo criterio que <c>SucursalScopeGuardPermisos</c>); las pruebas de integración cruzan ambos.
/// </summary>
public static class PermisosDimensiones
{
    public const string MovimientosTodasSucursales = "contabilidad.movimientos.gestionar-todas-sucursales";
}

/// <summary>
/// Alcance por sucursal (ADR-0051): operativo = sucursales con <c>UsuarioSucursal</c> activa; corporativo = permiso de bypass.
/// Misma regla y mismo código de error (<c>SUCURSAL_NO_ASOCIADA</c>) que <c>SucursalScopeGuard</c>.
/// </summary>
public sealed class AlcanceSucursalContable(ISucursalContabilidadPort sucursales, ICurrentUserContext usuario, ICurrentUserPermissions permisos)
{
    public async Task<bool> TodasAsync(CancellationToken ct) => await permisos.TieneAsync(PermisosDimensiones.MovimientosTodasSucursales, ct);

    public async Task VerificarAsync(Guid sucursalId, CancellationToken ct)
    {
        if (await TodasAsync(ct)) return;
        if (usuario.UserId is { } uid && await sucursales.UsuarioAsociadoAsync(uid, sucursalId, ct)) return;
        throw new ForbiddenException("SUCURSAL_NO_ASOCIADA", "No tienes acceso a los datos de esta sucursal.");
    }

    public async Task<IReadOnlyList<SucursalContable>> DisponiblesAsync(CancellationToken ct)
    {
        var todas = (await sucursales.ListarAsync(ct)).Where(s => s.Activa).OrderBy(s => s.Clave).ToList();
        if (await TodasAsync(ct)) return todas;
        if (usuario.UserId is not { } uid) return [];
        var propias = new List<SucursalContable>();
        foreach (var s in todas)
            if (await sucursales.UsuarioAsociadoAsync(uid, s.Id, ct)) propias.Add(s);
        return propias;
    }
}

/// <summary>Sucursales que el usuario puede elegir al capturar un movimiento.</summary>
public sealed record SucursalesDisponiblesQuery : IRequest<IReadOnlyList<SucursalContable>>;

public sealed class SucursalesDisponiblesHandler(AlcanceSucursalContable alcance) : IRequestHandler<SucursalesDisponiblesQuery, IReadOnlyList<SucursalContable>>
{
    public Task<IReadOnlyList<SucursalContable>> Handle(SucursalesDisponiblesQuery request, CancellationToken cancellationToken) =>
        alcance.DisponiblesAsync(cancellationToken);
}

/// <summary>Todas las sucursales de la empresa (para configurar centros por sucursal; no depende del alcance del usuario).</summary>
public sealed record ListarSucursalesContablesQuery : IRequest<IReadOnlyList<SucursalContable>>;

public sealed class ListarSucursalesContablesHandler(ISucursalContabilidadPort sucursales) : IRequestHandler<ListarSucursalesContablesQuery, IReadOnlyList<SucursalContable>>
{
    public Task<IReadOnlyList<SucursalContable>> Handle(ListarSucursalesContablesQuery request, CancellationToken cancellationToken) =>
        sucursales.ListarAsync(cancellationToken);
}

// ─── Centros de costo (Dim2) ↔ sucursal (D6) ─────────────────────────────────

public sealed record SucursalAsignadaDto(Guid Id, string Clave, string Nombre, bool Activa);

public sealed record CentroSucursalesResponse(
    Guid Dim2Id, string Clave, string Nombre, bool Activo, string Dim1Clave, IReadOnlyList<SucursalAsignadaDto> Sucursales);

/// <summary>CeCo (Dim2) con sus sucursales. <c>SoloSinSucursal</c> ayuda a ver qué falta asignar cuando llegue la relación de Millet.</summary>
public sealed record ListarCentrosSucursalQuery(string? Q, Guid? SucursalId, bool SoloSinSucursal, int Limit)
    : IRequest<IReadOnlyList<CentroSucursalesResponse>>;

public sealed class ListarCentrosSucursalHandler(ContabilidadDbContext db, ICentroCostoContabilidadPort centros, ISucursalContabilidadPort sucursales)
    : IRequestHandler<ListarCentrosSucursalQuery, IReadOnlyList<CentroSucursalesResponse>>
{
    public async Task<IReadOnlyList<CentroSucursalesResponse>> Handle(ListarCentrosSucursalQuery request, CancellationToken cancellationToken)
    {
        var asignaciones = await db.CentrosSucursal.AsNoTracking().ToListAsync(cancellationToken);
        IReadOnlyCollection<Guid>? dentro = request.SucursalId is { } sid
            ? [.. asignaciones.Where(a => a.SucursalId == sid).Select(a => a.Dim2Id).Distinct()]
            : null;
        if (dentro is { Count: 0 }) return [];
        var nodos = await centros.BuscarAsync(DimensionContable.Dim2, request.Q, dentro, incluirInactivos: true, request.Limit, cancellationToken);
        var catalogo = (await sucursales.ListarAsync(cancellationToken)).ToDictionary(s => s.Id);
        return [.. nodos
            .Select(n => new CentroSucursalesResponse(n.Id, n.Clave, n.Nombre, n.ActivoEnCadena, n.Dim1Clave,
                [.. asignaciones.Where(a => a.Dim2Id == n.Id && catalogo.ContainsKey(a.SucursalId))
                    .Select(a => catalogo[a.SucursalId]).OrderBy(s => s.Clave)
                    .Select(s => new SucursalAsignadaDto(s.Id, s.Clave, s.Nombre, s.Activa))]))
            .Where(c => !request.SoloSinSucursal || c.Sucursales.Count == 0)];
    }
}

/// <summary>Reemplaza el conjunto de sucursales de un CeCo (PUT idempotente). Lista vacía = el centro no se usa en ninguna sucursal.</summary>
public sealed record AsignarSucursalesCentroCommand(Guid Dim2Id, IReadOnlyList<Guid> SucursalIds) : IRequest<CentroSucursalesResponse>;

public sealed class AsignarSucursalesCentroHandler(
    ContabilidadDbContext db, ICentroCostoContabilidadPort centros, ISucursalContabilidadPort sucursales)
    : IRequestHandler<AsignarSucursalesCentroCommand, CentroSucursalesResponse>
{
    public async Task<CentroSucursalesResponse> Handle(AsignarSucursalesCentroCommand request, CancellationToken cancellationToken)
    {
        var nodo = (await centros.ObtenerAsync([request.Dim2Id], cancellationToken)).GetValueOrDefault(request.Dim2Id);
        if (nodo is not { Nivel: DimensionContable.Dim2 })
            throw new EntityNotFoundException("CONTAB_CENTRO_NO_ENCONTRADO", "El centro de costo (Dimensión 2) no existe.");
        var catalogo = (await sucursales.ListarAsync(cancellationToken)).ToDictionary(s => s.Id);
        var pedidas = request.SucursalIds.Distinct().ToList();
        var faltante = pedidas.FirstOrDefault(id => !catalogo.ContainsKey(id));
        if (faltante != Guid.Empty)
            throw new BusinessRuleException("CONTAB_CENTRO_SUCURSAL_INVALIDO", "Una de las sucursales indicadas no existe en la empresa.");

        var actuales = await db.CentrosSucursal.Where(x => x.Dim2Id == request.Dim2Id).ToListAsync(cancellationToken);
        db.CentrosSucursal.RemoveRange(actuales.Where(a => !pedidas.Contains(a.SucursalId)));
        foreach (var sid in pedidas.Where(s => actuales.All(a => a.SucursalId != s)))
            db.CentrosSucursal.Add(new CentroCostoSucursal(Guid.CreateVersion7(), request.Dim2Id, sid));
        await db.SaveChangesAsync(cancellationToken);

        return new CentroSucursalesResponse(nodo.Id, nodo.Clave, nodo.Nombre, nodo.ActivoEnCadena, nodo.Dim1Clave,
            [.. pedidas.Select(id => catalogo[id]).OrderBy(s => s.Clave).Select(s => new SucursalAsignadaDto(s.Id, s.Clave, s.Nombre, s.Activa))]);
    }
}

// ─── Selector de centros para capturar un movimiento ─────────────────────────

public sealed record CentroOpcionDto(Guid Id, DimensionContable Nivel, string Clave, string Nombre, Guid Dim1Id, Guid? Dim2Id, string Dim1Clave, string? Dim2Clave);

/// <summary>
/// Solo centros activos de la sucursal elegida (D6: "un centro de otra sucursal no es seleccionable"). Con la exigencia
/// apagada se ofrecen todos los activos. Verifica que el usuario pueda operar esa sucursal.
/// </summary>
public sealed record CentrosParaMovimientoQuery(Guid SucursalId, DimensionContable Nivel, Guid? Dim2Id, string? Q, int Limit)
    : IRequest<IReadOnlyList<CentroOpcionDto>>;

public sealed class CentrosParaMovimientoHandler(
    ContabilidadDbContext db, ICentroCostoContabilidadPort centros, AlcanceSucursalContable alcance,
    Microsoft.Extensions.Options.IOptions<DimensionesOpciones> opciones)
    : IRequestHandler<CentrosParaMovimientoQuery, IReadOnlyList<CentroOpcionDto>>
{
    public async Task<IReadOnlyList<CentroOpcionDto>> Handle(CentrosParaMovimientoQuery request, CancellationToken cancellationToken)
    {
        await alcance.VerificarAsync(request.SucursalId, cancellationToken);
        List<Guid>? dentro = null;
        if (opciones.Value.ExigirSucursalDelCentro)
        {
            dentro = await db.CentrosSucursal.AsNoTracking().Where(x => x.SucursalId == request.SucursalId)
                .Select(x => x.Dim2Id).Distinct().ToListAsync(cancellationToken);
            if (dentro.Count == 0) return [];
        }
        if (request.Dim2Id is { } d2) dentro = dentro is null || dentro.Contains(d2) ? [d2] : [];
        if (dentro is { Count: 0 }) return [];
        var nodos = await centros.BuscarAsync(request.Nivel, request.Q, dentro, incluirInactivos: false, request.Limit, cancellationToken);
        return [.. nodos.Where(n => n.ActivoEnCadena)
            .Select(n => new CentroOpcionDto(n.Id, n.Nivel, n.Clave, n.Nombre, n.Dim1Id, n.Dim2Id, n.Dim1Clave, n.Dim2Clave))];
    }
}
