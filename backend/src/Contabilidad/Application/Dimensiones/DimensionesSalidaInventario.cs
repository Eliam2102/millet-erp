using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Contabilidad.Application.Dimensiones;

/// <summary>
/// H1 (86e3z5w25) · <see cref="IDimensionesSalidaInventarioPort"/>. La cuenta se resuelve como la resolverá el motor: plantilla vigente
/// del evento, línea de cargo del campo que toca a la causa (<see cref="NormalizadorEventos.CampoSalidaInventario"/>) y su rol en
/// <c>roles_cuenta</c> (sucursal antes que el genérico, MXN, vigente). Las exigencias salen de <see cref="ValidadorDimensiones"/> con el
/// tipo de operación de la plantilla, y la validación final (jerarquía, estado, ubicación de la sucursal) es la misma de la póliza.
/// Sin plantilla, sin rol o sin cuenta no hay exigencia que revisar: la póliza irá a Rechazos por su propia causa.
/// Corre sin usuario (lector de la app): todo se acota a la empresa de la solicitud.
/// </summary>
public sealed class DimensionesSalidaInventario(
    ContabilidadDbContext db, ICentroCostoContabilidadPort centros, ValidadorDimensiones validador, IOptions<DimensionesOpciones> opciones)
    : IDimensionesSalidaInventarioPort
{
    private const int LimiteBusqueda = 500;

    public async Task<DimensionesSalidaResultado> ResolverAsync(SolicitudDimensionesSalida solicitud, CancellationToken ct)
    {
        var s = solicitud;
        var areaClave = Limpia(s.AreaClave);
        var equipoClave = Limpia(s.EquipoClave);

        // 1. Claves → centros de la sucursal (mismo alcance que el selector de captura de movimientos).
        AlcanceCentros? alcance = null;
        if (opciones.Value.ExigirSucursalDelCentro)
        {
            var dim1 = await db.UbicacionesSucursal.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.EmpresaId == s.EmpresaId && x.SucursalId == s.SucursalId).Select(x => x.Dim1Id).ToListAsync(ct);
            var corporativos = await db.CentrosCorporativos.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.EmpresaId == s.EmpresaId).Select(x => x.Dim2Id).ToListAsync(ct);
            alcance = new AlcanceCentros(dim1, corporativos);
        }
        CentroCostoNodo? area = null, equipo = null;
        if (areaClave is not null)
        {
            area = await BuscarAsync(DimensionContable.Dim2, areaClave, alcance, ct);
            if (area is null)
                return Invalido($"El área {areaClave} no existe, está dada de baja o no es de la sucursal del movimiento.");
        }
        if (equipoClave is not null)
        {
            equipo = await BuscarAsync(DimensionContable.Dim3, equipoClave, alcance, ct);
            if (equipo is null)
                return Invalido($"El equipo {equipoClave} no existe, está dado de baja o no es de la sucursal del movimiento.");
            if (area is not null && equipo.Dim2Id != area.Id)
                return Invalido($"El equipo {equipoClave} no pertenece al área {areaClave}.");
        }
        var centro = equipo?.Id ?? area?.Id;

        // 2. Cuenta que cargará la póliza por esta causa (86e3zf04y: la rotura de la pantalla tiene plantilla propia).
        var tipoEvento = s.TipoEvento ?? NormalizadorEventos.SalidaInventarioRegistrada;
        var plantilla = await db.PlantillasContables.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.EmpresaId == s.EmpresaId && p.TipoEvento == tipoEvento && p.VigenteDesde <= s.Fecha)
            .OrderByDescending(p => p.NumeroVersion).FirstOrDefaultAsync(ct);
        var campo = s.Campo ?? NormalizadorEventos.CampoSalidaInventario(s.Causa);
        var linea = plantilla?.Lineas.FirstOrDefault(l => l.Lado == LadoPartida.Cargo && l.Campo == campo)
            ?? plantilla?.Lineas.FirstOrDefault(l => l.Lado == LadoPartida.Cargo && l.Campo == "Subtotal"); // plantilla v1 (#445)
        if (plantilla is null || linea is null) return new(null, null, centro, false, false, null);
        var roles = await db.RolesCuenta.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.EmpresaId == s.EmpresaId && r.DeletedAt == null && r.Codigo == linea.Rol).ToListAsync(ct);
        if (GenerarPolizaDesdeEventoHandler.Rol(roles, linea.Rol, s.SucursalId, Poliza.MonedaNacional, s.Fecha)?.CuentaId is not { } cuentaId)
            return new(null, null, centro, false, false, null);
        var cuenta = await db.Cuentas.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == cuentaId).Select(c => c.Codigo).FirstOrDefaultAsync(ct);
        var clave = plantilla.TipoOperacionClave.Trim().ToUpperInvariant();
        var tipo = await db.TiposDocumento.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.EmpresaId == s.EmpresaId && t.Clave == clave).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);

        // 3. Exigencias C-03 de la cuenta y validación final con los centros capturados (la misma de la póliza).
        var requerimientos = await validador.ResolverAsync(cuentaId, tipo, s.Fecha, ct);
        var exigeArea = Exige(requerimientos, DimensionContable.Dim2);
        var exigeEquipo = Exige(requerimientos, DimensionContable.Dim3);
        if (tipo is { } tipoId && centro is not null)
        {
            var v = await validador.ValidarAsync(new MovimientoDimensionado(cuentaId, tipoId, s.Fecha, s.SucursalId,
                null, equipo is null ? area?.Id : null, equipo?.Id, OrigenMovimiento.Automatica), ct);
            var invalido = v.Errores.FirstOrDefault(e => e.Codigo != "CONTAB_DIM_OBLIGATORIA_FALTANTE" && e.Campo is "dim1Id" or "dim2Id" or "dim3Id");
            if (invalido is not null) return Invalido(invalido.Mensaje);
        }
        var faltan = new List<string>();
        if (exigeArea && centro is null) faltan.Add("el área");
        if (exigeEquipo && equipo is null) faltan.Add("el equipo");
        var faltante = faltan.Count == 0 ? null
            : $"Falta {string.Join(" y ", faltan)}: la cuenta {cuenta} de esta salida lo exige (regla de dimensiones C-03).";
        return new(null, faltante, centro, exigeArea, exigeEquipo, cuenta);
    }

    private async Task<CentroCostoNodo?> BuscarAsync(DimensionContable nivel, string clave, AlcanceCentros? alcance, CancellationToken ct) =>
        (await centros.BuscarAsync(nivel, clave, alcance, incluirInactivos: false, LimiteBusqueda, ct))
            .FirstOrDefault(n => n.ActivoEnCadena && string.Equals(n.Clave.Trim(), clave, StringComparison.OrdinalIgnoreCase));

    private static bool Exige(IReadOnlyList<RequerimientoEfectivo> requerimientos, DimensionContable d) =>
        requerimientos.Any(r => r.Dimension == d && r.Requerimiento == RequerimientoDimension.Obligatorio);

    private static string? Limpia(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static DimensionesSalidaResultado Invalido(string error) => new(error, null, null, false, false, null);
}
