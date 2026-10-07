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

// ─── Ubicación (Dim1) → sucursal y centros corporativos (K10.2, V49) ─────────

public sealed record SucursalAsignadaDto(Guid Id, string Clave, string Nombre, bool Activa);

public sealed record UbicacionSucursalResponse(Guid Dim1Id, string Clave, string Nombre, bool Activo, SucursalAsignadaDto? Sucursal);

/// <summary>Todas las ubicaciones (Dim1) con la sucursal a la que están ligadas (null = sin ligar: sus centros no se usan).</summary>
public sealed record ListarUbicacionesSucursalQuery : IRequest<IReadOnlyList<UbicacionSucursalResponse>>;

public sealed class ListarUbicacionesSucursalHandler(ContabilidadDbContext db, ICentroCostoContabilidadPort centros, ISucursalContabilidadPort sucursales)
    : IRequestHandler<ListarUbicacionesSucursalQuery, IReadOnlyList<UbicacionSucursalResponse>>
{
    public async Task<IReadOnlyList<UbicacionSucursalResponse>> Handle(ListarUbicacionesSucursalQuery request, CancellationToken cancellationToken)
    {
        var ubicaciones = await centros.BuscarAsync(DimensionContable.Dim1, null, null, incluirInactivos: true, 500, cancellationToken);
        var ligadas = await db.UbicacionesSucursal.AsNoTracking().ToDictionaryAsync(x => x.Dim1Id, x => x.SucursalId, cancellationToken);
        var catalogo = (await sucursales.ListarAsync(cancellationToken)).ToDictionary(s => s.Id);
        return [.. ubicaciones.Select(u => new UbicacionSucursalResponse(u.Id, u.Clave, u.Nombre, u.Activo,
            ligadas.TryGetValue(u.Id, out var sid) && catalogo.TryGetValue(sid, out var suc)
                ? new SucursalAsignadaDto(suc.Id, suc.Clave, suc.Nombre, suc.Activa) : null))];
    }
}

/// <summary>Liga (o desliga con null) una ubicación a una sucursal. Idempotente.</summary>
public sealed record AsignarSucursalUbicacionCommand(Guid Dim1Id, Guid? SucursalId) : IRequest<UbicacionSucursalResponse>;

public sealed class AsignarSucursalUbicacionHandler(ContabilidadDbContext db, ICentroCostoContabilidadPort centros, ISucursalContabilidadPort sucursales)
    : IRequestHandler<AsignarSucursalUbicacionCommand, UbicacionSucursalResponse>
{
    public async Task<UbicacionSucursalResponse> Handle(AsignarSucursalUbicacionCommand request, CancellationToken cancellationToken)
    {
        var nodo = (await centros.ObtenerAsync([request.Dim1Id], cancellationToken)).GetValueOrDefault(request.Dim1Id);
        if (nodo is not { Nivel: DimensionContable.Dim1 })
            throw new EntityNotFoundException("CONTAB_UBICACION_NO_ENCONTRADA", "La ubicación (Dimensión 1) no existe.");
        SucursalContable? suc = null;
        if (request.SucursalId is { } sid)
            suc = (await sucursales.ListarAsync(cancellationToken)).FirstOrDefault(s => s.Id == sid)
                ?? throw new BusinessRuleException("CONTAB_UBICACION_SUCURSAL_INVALIDA", "La sucursal indicada no existe en la empresa.");

        var actual = await db.UbicacionesSucursal.FirstOrDefaultAsync(x => x.Dim1Id == request.Dim1Id, cancellationToken);
        if (suc is null) { if (actual is not null) db.UbicacionesSucursal.Remove(actual); }
        else if (actual is null) db.UbicacionesSucursal.Add(new UbicacionSucursal(Guid.CreateVersion7(), request.Dim1Id, suc.Id));
        else actual.CambiarSucursal(suc.Id);
        await db.SaveChangesAsync(cancellationToken);
        return new UbicacionSucursalResponse(nodo.Id, nodo.Clave, nodo.Nombre, nodo.Activo,
            suc is null ? null : new SucursalAsignadaDto(suc.Id, suc.Clave, suc.Nombre, suc.Activa));
    }
}

public sealed record CentroCorporativoResponse(Guid Dim2Id, string Clave, string Nombre, bool Activo, string Dim1Clave, bool Corporativo);

/// <summary>CeCo (Dim2) con su marca de corporativo. <c>SoloCorporativos</c> lista solo los marcados.</summary>
public sealed record ListarCentrosCorporativosQuery(string? Q, bool SoloCorporativos, int Limit) : IRequest<IReadOnlyList<CentroCorporativoResponse>>;

public sealed class ListarCentrosCorporativosHandler(ContabilidadDbContext db, ICentroCostoContabilidadPort centros)
    : IRequestHandler<ListarCentrosCorporativosQuery, IReadOnlyList<CentroCorporativoResponse>>
{
    public async Task<IReadOnlyList<CentroCorporativoResponse>> Handle(ListarCentrosCorporativosQuery request, CancellationToken cancellationToken)
    {
        var marcados = (await db.CentrosCorporativos.AsNoTracking().Select(x => x.Dim2Id).ToListAsync(cancellationToken)).ToHashSet();
        if (request.SoloCorporativos && marcados.Count == 0) return [];
        var alcance = request.SoloCorporativos ? new AlcanceCentros([], marcados) : null;
        var nodos = await centros.BuscarAsync(DimensionContable.Dim2, request.Q, alcance, incluirInactivos: true, request.Limit, cancellationToken);
        return [.. nodos.Select(n => new CentroCorporativoResponse(n.Id, n.Clave, n.Nombre, n.ActivoEnCadena, n.Dim1Clave, marcados.Contains(n.Id)))];
    }
}

public sealed record MarcarCentroCorporativoCommand(Guid Dim2Id, bool Corporativo) : IRequest<CentroCorporativoResponse>;

public sealed class MarcarCentroCorporativoHandler(ContabilidadDbContext db, ICentroCostoContabilidadPort centros)
    : IRequestHandler<MarcarCentroCorporativoCommand, CentroCorporativoResponse>
{
    public async Task<CentroCorporativoResponse> Handle(MarcarCentroCorporativoCommand request, CancellationToken cancellationToken)
    {
        var nodo = (await centros.ObtenerAsync([request.Dim2Id], cancellationToken)).GetValueOrDefault(request.Dim2Id);
        if (nodo is not { Nivel: DimensionContable.Dim2 })
            throw new EntityNotFoundException("CONTAB_CENTRO_NO_ENCONTRADO", "El centro de costo (Dimensión 2) no existe.");
        var actual = await db.CentrosCorporativos.FirstOrDefaultAsync(x => x.Dim2Id == request.Dim2Id, cancellationToken);
        if (request.Corporativo && actual is null) db.CentrosCorporativos.Add(new CentroCorporativo(Guid.CreateVersion7(), request.Dim2Id));
        if (!request.Corporativo && actual is not null) db.CentrosCorporativos.Remove(actual);
        await db.SaveChangesAsync(cancellationToken);
        return new CentroCorporativoResponse(nodo.Id, nodo.Clave, nodo.Nombre, nodo.ActivoEnCadena, nodo.Dim1Clave, request.Corporativo);
    }
}

// ─── Selectores para capturar un movimiento ──────────────────────────────────

public sealed record CentroOpcionDto(Guid Id, DimensionContable Nivel, string Clave, string Nombre, Guid Dim1Id, Guid? Dim2Id, string Dim1Clave, string? Dim2Clave);

/// <summary>
/// Solo centros activos de la sucursal elegida: los de sus ubicaciones más los corporativos (K10.2: "un centro de otra sucursal
/// no es seleccionable"). Con la exigencia apagada se ofrecen todos los activos. Verifica que el usuario pueda operar la sucursal.
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
        if (request.Nivel is not (DimensionContable.Dim1 or DimensionContable.Dim2 or DimensionContable.Dim3))
            throw new BusinessRuleException("CONTAB_DIM_NIVEL_INVALIDO", "Solo las dimensiones 1, 2 y 3 son centros de costo.");
        AlcanceCentros? permitido = null;
        if (opciones.Value.ExigirSucursalDelCentro)
        {
            var dim1 = await db.UbicacionesSucursal.AsNoTracking().Where(x => x.SucursalId == request.SucursalId).Select(x => x.Dim1Id).ToListAsync(cancellationToken);
            var corporativos = await db.CentrosCorporativos.AsNoTracking().Select(x => x.Dim2Id).ToListAsync(cancellationToken);
            if (dim1.Count == 0 && corporativos.Count == 0) return [];
            permitido = new AlcanceCentros(dim1, corporativos);
        }
        var nodos = await centros.BuscarAsync(request.Nivel, request.Q, permitido, incluirInactivos: false, request.Limit, cancellationToken);
        return [.. nodos.Where(n => n.ActivoEnCadena && (request.Dim2Id is null || n.Dim2Id == request.Dim2Id))
            .Select(n => new CentroOpcionDto(n.Id, n.Nivel, n.Clave, n.Nombre, n.Dim1Id, n.Dim2Id, n.Dim1Clave, n.Dim2Clave))];
    }
}

/// <summary>Clientes, proveedores o cuentas bancarias activos para la captura (dimensiones auxiliares de K10.2).</summary>
public sealed record AuxiliaresParaMovimientoQuery(TipoAuxiliar Tipo, string? Q, int Limit) : IRequest<IReadOnlyList<AuxiliarContable>>;

public sealed class AuxiliaresParaMovimientoHandler(ITerceroContabilidadPort terceros, ICuentaBancariaContabilidadPort bancos)
    : IRequestHandler<AuxiliaresParaMovimientoQuery, IReadOnlyList<AuxiliarContable>>
{
    public async Task<IReadOnlyList<AuxiliarContable>> Handle(AuxiliaresParaMovimientoQuery request, CancellationToken cancellationToken)
    {
        var lista = request.Tipo == TipoAuxiliar.Banco
            ? await bancos.BuscarAsync(request.Q, request.Limit, cancellationToken)
            : await terceros.BuscarAsync(request.Tipo, request.Q, request.Limit, cancellationToken);
        return [.. lista.Where(a => a.Activo)];
    }
}
