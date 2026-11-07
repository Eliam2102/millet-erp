using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Contabilidad.Application.Dimensiones;

/// <summary>
/// H1 pasos 3 y 4 (86e3z5w25) · <see cref="IDimensionesCapturaPort"/>. La cuenta se resuelve como la resolverá el motor: plantilla
/// vigente del evento, línea de cargo del campo y su rol (por clasificador si la línea lo es; sucursal antes que el genérico, MXN,
/// vigente). Las exigencias salen de <see cref="ValidadorDimensiones"/> con el tipo de operación de la plantilla, y la validación
/// final (jerarquía, estado, ubicación de la sucursal) es la misma de la póliza. Todo se acota a la empresa de la solicitud.
/// Se expone con <c>DimensionesCapturaAdapter</c> (Infrastructure/PublicAdapters), que abre el bypass del filtro global sin usuario.
/// </summary>
public sealed class DimensionesCaptura(
    ContabilidadDbContext db, ICentroCostoContabilidadPort centros, ValidadorDimensiones validador, IOptions<DimensionesOpciones> opciones)
    : IDimensionesCapturaPort
{
    public async Task<ExigenciaDimensiones> ExigenciasAsync(MovimientoCaptura movimiento, CancellationToken ct) =>
        (await CuentaAsync(movimiento, ct)).Exigencia;

    public async Task<IReadOnlyList<CentroOpcionDto>> CentrosAsync(Guid empresaId, Guid sucursalId, bool equipos, Guid? areaId, string? q,
        int limite, CancellationToken ct)
    {
        var alcance = await AlcanceAsync(empresaId, sucursalId, ct);
        if (alcance is { Dim1Ids.Count: 0, Dim2Ids.Count: 0 }) return [];
        var nodos = await centros.BuscarAsync(equipos ? DimensionContable.Dim3 : DimensionContable.Dim2, q, alcance, incluirInactivos: false,
            Math.Clamp(limite, 1, 200), ct);
        return [.. nodos.Where(n => n.ActivoEnCadena && (areaId is null || n.Dim2Id == areaId))
            .Select(n => new CentroOpcionDto(n.Id, n.Nivel, n.Clave, n.Nombre, n.Dim1Id, n.Dim2Id, n.Dim1Clave, n.Dim2Clave))];
    }

    public async Task<DimensionesCapturaResultado> ValidarAsync(MovimientoCaptura movimiento, Guid? areaId, Guid? equipoId, CancellationToken ct)
    {
        var m = movimiento;
        var ids = new[] { areaId, equipoId }.OfType<Guid>().ToArray();
        var nodos = ids.Length == 0 ? new Dictionary<Guid, CentroCostoNodo>() : await centros.ObtenerAsync(ids, ct);
        var alcance = ids.Length == 0 ? null : await AlcanceAsync(m.EmpresaId, m.SucursalId, ct);
        CentroCostoNodo? area = null, equipo = null;
        if (areaId is { } a)
        {
            area = nodos.GetValueOrDefault(a);
            if (area is not { Nivel: DimensionContable.Dim2, ActivoEnCadena: true } || !EnAlcance(area, alcance))
                return Invalido("El área elegida no existe, está dada de baja o no es de la sucursal del movimiento.");
        }
        if (equipoId is { } q)
        {
            equipo = nodos.GetValueOrDefault(q);
            if (equipo is not { Nivel: DimensionContable.Dim3, ActivoEnCadena: true } || !EnAlcance(equipo, alcance))
                return Invalido("El equipo elegido no existe, está dado de baja o no es de la sucursal del movimiento.");
            if (area is not null && equipo.Dim2Id != area.Id)
                return Invalido($"El equipo {equipo.Clave} no pertenece al área {area.Clave}.");
        }
        var centro = equipo?.Id ?? area?.Id;

        var (cuentaId, tipoId, exigencia) = await CuentaAsync(m, ct);
        if (cuentaId is { } cuenta && tipoId is { } tipo && centro is not null)
        {
            var v = await validador.ValidarAsync(new MovimientoDimensionado(cuenta, tipo, m.Fecha, m.SucursalId,
                null, equipo is null ? area?.Id : null, equipo?.Id, OrigenMovimiento.Automatica), ct);
            var invalido = v.Errores.FirstOrDefault(e => e.Codigo != "CONTAB_DIM_OBLIGATORIA_FALTANTE" && e.Campo is "dim1Id" or "dim2Id" or "dim3Id");
            if (invalido is not null) return Invalido(invalido.Mensaje);
        }
        var faltan = new List<string>();
        if (exigencia.ExigeArea && centro is null) faltan.Add("el área");
        if (exigencia.ExigeEquipo && equipo is null) faltan.Add("el equipo");
        var faltante = faltan.Count == 0 ? null
            : $"Falta {string.Join(" y ", faltan)}: la cuenta {exigencia.CuentaCodigo} lo exige (regla de dimensiones C-03). La póliza quedará en Rechazos.";
        return new(null, faltante, centro, exigencia);
    }

    /// <summary>Cuenta que cargará la póliza, tipo de operación de la plantilla y exigencias C-03 de esa cuenta.</summary>
    private async Task<(Guid? CuentaId, Guid? TipoId, ExigenciaDimensiones Exigencia)> CuentaAsync(MovimientoCaptura m, CancellationToken ct)
    {
        var plantilla = await db.PlantillasContables.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.EmpresaId == m.EmpresaId && p.TipoEvento == m.TipoEvento && p.VigenteDesde <= m.Fecha)
            .OrderByDescending(p => p.NumeroVersion).FirstOrDefaultAsync(ct);
        var linea = plantilla?.Lineas.FirstOrDefault(l => l.Lado == LadoPartida.Cargo && l.Campo == m.Campo);
        if (plantilla is null || linea is null) return (null, null, ExigenciaDimensiones.Ninguna);

        var prefijo = linea.Rol + "_";
        var roles = await db.RolesCuenta.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.EmpresaId == m.EmpresaId && r.DeletedAt == null
                && (r.Codigo == linea.Rol || (linea.RolPorClasificador && r.Codigo.StartsWith(prefijo))))
            .ToListAsync(ct);
        var cuentaId = linea.RolPorClasificador
            ? GenerarPolizaDesdeEventoHandler.CuentaPorClasificador(roles, linea.Rol, m.Clasificador, m.SucursalId, Poliza.MonedaNacional, m.Fecha)
            : GenerarPolizaDesdeEventoHandler.Rol(roles, linea.Rol, m.SucursalId, Poliza.MonedaNacional, m.Fecha)?.CuentaId;
        if (cuentaId is not { } id) return (null, null, ExigenciaDimensiones.Ninguna);

        var codigo = await db.Cuentas.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == id).Select(c => c.Codigo).FirstOrDefaultAsync(ct);
        var clave = plantilla.TipoOperacionClave.Trim().ToUpperInvariant();
        var tipo = await db.TiposDocumento.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.EmpresaId == m.EmpresaId && t.Clave == clave).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        var requerimientos = await validador.ResolverAsync(id, tipo, m.Fecha, ct);
        return (id, tipo, new ExigenciaDimensiones(Exige(requerimientos, DimensionContable.Dim2), Exige(requerimientos, DimensionContable.Dim3), codigo));
    }

    /// <summary>Ubicaciones de la sucursal y CeCo corporativos; null con la exigencia de sucursal apagada (todos los activos).</summary>
    private async Task<AlcanceCentros?> AlcanceAsync(Guid empresaId, Guid sucursalId, CancellationToken ct)
    {
        if (!opciones.Value.ExigirSucursalDelCentro) return null;
        var dim1 = await db.UbicacionesSucursal.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.EmpresaId == empresaId && x.SucursalId == sucursalId).Select(x => x.Dim1Id).ToListAsync(ct);
        var corporativos = await db.CentrosCorporativos.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.EmpresaId == empresaId).Select(x => x.Dim2Id).ToListAsync(ct);
        return new AlcanceCentros(dim1, corporativos);
    }

    private static bool EnAlcance(CentroCostoNodo n, AlcanceCentros? alcance) =>
        alcance is null || alcance.Dim1Ids.Contains(n.Dim1Id) || (n.Dim2Id is { } d2 && alcance.Dim2Ids.Contains(d2));

    private static bool Exige(IReadOnlyList<RequerimientoEfectivo> requerimientos, DimensionContable d) =>
        requerimientos.Any(r => r.Dimension == d && r.Requerimiento == RequerimientoDimension.Obligatorio);

    private static DimensionesCapturaResultado Invalido(string error) => new(error, null, null, ExigenciaDimensiones.Ninguna);
}
