using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Contabilidad.Application.Dimensiones;

/// <summary>
/// H1 paso 2 (86e3z5w25) · <see cref="IDimensionesGastoPort"/>. La cuenta se resuelve como la resolverá el motor: plantilla vigente de
/// <see cref="NormalizadorEventos.GastoRegistrado"/>, su línea de cargo «Gasto» y el rol en <c>roles_cuenta</c> (sucursal antes que el
/// genérico, MXN, vigente). El alcance de centros es el del selector de captura de movimientos (K10.2/V49). Todo se acota a la
/// empresa recibida: lo expone un adapter con bypass del filtro de empresa.
/// </summary>
public sealed class DimensionesGasto(
    ContabilidadDbContext db, ICentroCostoContabilidadPort centros, ValidadorDimensiones validador, IOptions<DimensionesOpciones> opciones)
    : IDimensionesGastoPort
{
    private const string CampoGasto = "Gasto";

    public async Task<ExigenciaDimensionesGasto> ExigenciasAsync(Guid empresaId, Guid sucursalId, DateOnly fecha, CancellationToken ct)
    {
        var plantilla = await db.PlantillasContables.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.TipoEvento == NormalizadorEventos.GastoRegistrado && p.VigenteDesde <= fecha)
            .OrderByDescending(p => p.NumeroVersion).FirstOrDefaultAsync(ct);
        var linea = plantilla?.Lineas.FirstOrDefault(l => l.Lado == LadoPartida.Cargo && l.Campo == CampoGasto);
        if (plantilla is null || linea is null) return new(false, false, null);
        var roles = await db.RolesCuenta.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.EmpresaId == empresaId && r.DeletedAt == null && r.Codigo == linea.Rol).ToListAsync(ct);
        if (GenerarPolizaDesdeEventoHandler.Rol(roles, linea.Rol, sucursalId, Poliza.MonedaNacional, fecha)?.CuentaId is not { } cuentaId)
            return new(false, false, null);
        var cuenta = await db.Cuentas.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == cuentaId).Select(c => c.Codigo).FirstOrDefaultAsync(ct);
        var clave = plantilla.TipoOperacionClave.Trim().ToUpperInvariant();
        var tipo = await db.TiposDocumento.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.EmpresaId == empresaId && t.Clave == clave).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        var requerimientos = await validador.ResolverAsync(cuentaId, tipo, fecha, ct);
        return new(Exige(requerimientos, DimensionContable.Dim2), Exige(requerimientos, DimensionContable.Dim3), cuenta);
    }

    public async Task<IReadOnlyList<CentroGastoOpcion>> CentrosAsync(
        Guid empresaId, Guid sucursalId, int nivel, Guid? dim2Id, string? q, int limite, CancellationToken ct)
    {
        var dimension = nivel == 3 ? DimensionContable.Dim3 : DimensionContable.Dim2;
        var alcance = await AlcanceAsync(empresaId, sucursalId, ct);
        if (alcance is { Dim1Ids.Count: 0, Dim2Ids.Count: 0 }) return [];
        var nodos = await centros.BuscarAsync(dimension, string.IsNullOrWhiteSpace(q) ? null : q.Trim(), alcance,
            incluirInactivos: false, Math.Clamp(limite, 1, 200), ct);
        return [.. nodos.Where(n => n.ActivoEnCadena && (dim2Id is null || n.Dim2Id == dim2Id))
            .Select(n => new CentroGastoOpcion(n.Id, nivel == 3 ? 3 : 2, n.Clave, n.Nombre, n.Dim2Id, n.Dim2Clave))];
    }

    public async Task<string?> ValidarCentroAsync(Guid empresaId, Guid sucursalId, Guid centroCostoId, CancellationToken ct)
    {
        var nodo = (await centros.ObtenerAsync([centroCostoId], ct)).GetValueOrDefault(centroCostoId);
        if (nodo is null || nodo.Nivel is not (DimensionContable.Dim2 or DimensionContable.Dim3))
            return "El área o equipo capturado no existe.";
        if (!nodo.ActivoEnCadena)
            return $"El centro {nodo.Clave} está dado de baja.";
        var alcance = await AlcanceAsync(empresaId, sucursalId, ct);
        if (alcance is not null && !alcance.Dim1Ids.Contains(nodo.Dim1Id) && !(nodo.Dim2Id is { } d2 && alcance.Dim2Ids.Contains(d2)))
            return $"El centro {nodo.Clave} no es de la sucursal del gasto.";
        return null;
    }

    /// <summary>Null = sin restricción de sucursal (exigencia apagada).</summary>
    private async Task<AlcanceCentros?> AlcanceAsync(Guid empresaId, Guid sucursalId, CancellationToken ct)
    {
        if (!opciones.Value.ExigirSucursalDelCentro) return null;
        var dim1 = await db.UbicacionesSucursal.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.EmpresaId == empresaId && x.SucursalId == sucursalId).Select(x => x.Dim1Id).ToListAsync(ct);
        var corporativos = await db.CentrosCorporativos.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.EmpresaId == empresaId).Select(x => x.Dim2Id).ToListAsync(ct);
        return new AlcanceCentros(dim1, corporativos);
    }

    private static bool Exige(IReadOnlyList<RequerimientoEfectivo> requerimientos, DimensionContable d) =>
        requerimientos.Any(r => r.Dimension == d && r.Requerimiento == RequerimientoDimension.Obligatorio);
}
