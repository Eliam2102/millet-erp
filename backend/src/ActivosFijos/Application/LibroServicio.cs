using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ActivosFijos.Application;

public sealed class LibroServicio(ActivosFijosDbContext db, ActivoServicio acceso, ICurrentEmpresaContext empresa,
    ITransaccionPolizaActivo transaccion, IPolizaContablePort polizas, IPeriodoContableConsultaPort periodos,
    ICentroCostoContabilidadPort centros, ICatalogosActivoReadPort? catalogos = null)
{
    public Guid Empresa => empresa.Current ?? throw new UnauthorizedAccessException();
    public async Task<bool> MismoMesAsync(CancellationToken ct) =>
        await db.Set<ConfiguracionLibro>().Select(x => (bool?)x.DepreciarMismoMes).SingleOrDefaultAsync(ct) ?? false;
    public async Task<DateOnly?> PrimerMesAsync(CancellationToken ct)
    {
        var mismoMes = await MismoMesAsync(ct);
        var activos = await db.Activos.Where(x => x.Estado == EstadoActivo.Activo && x.InicioUso != null)
            .Join(db.Clases.Where(c => c.Depreciable && c.TasaFiscalAnual != null && c.TasaFiscalAnual > 0), a => a.ClaseId, c => c.Id, (a, c) => a).ToListAsync(ct);
        return activos.Select(a => (DateOnly?)CalculoDepreciacion.PrimerMes(a, mismoMes)).Min();
    }
    public async Task BloquearAsync(CancellationToken ct)
    {
        var llave = $"AF-LIBRO-{Empresa}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({llave}, 0))", ct);
    }
    public async Task PeriodoAsync(DateOnly fecha, CancellationToken ct)
    {
        if (!(await periodos.ConsultarPorFechaAsync(fecha, ct)).AdmiteMovimientos)
            throw new BusinessRuleException("AF_PERIODO_CERRADO", "El periodo no está abierto en Contabilidad.");
    }
    public async Task FechaModificableAsync(DateOnly fecha, CancellationToken ct)
    {
        if (await db.Set<CorridaDepreciacion>().AnyAsync(x => !x.Anulada && x.Periodo >= CalculoDepreciacion.Mes(fecha), ct))
            throw new BusinessRuleException("AF_PERIODO_YA_DEPRECIADO", "Anule primero las corridas desde este periodo para modificar su libro.");
    }
    public async Task<IReadOnlyList<DetalleDepreciacion>> PreviaAsync(DateOnly periodo, bool escribir, CancellationToken ct)
    {
        var mes = CalculoDepreciacion.Mes(periodo);
        var corrida = await db.Set<CorridaDepreciacion>().AsNoTracking().Include(x => x.Detalles).SingleOrDefaultAsync(x => !x.Anulada && x.Periodo == mes, ct);
        if (corrida == null) return await PreviaInternaAsync(periodo, true, ct, escribir);
        foreach (var sucursal in corrida.Detalles.Select(x => x.SucursalId).Distinct()) await acceso.SucursalAsync(sucursal, escribir, ct);
        return corrida.Detalles;
    }
    public async Task<CorridaDepreciacion> ConfirmarAsync(DateOnly periodo, CancellationToken ct)
    {
        await acceso.PermisoAsync("activos_fijos.depreciacion.correr", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await BloquearAsync(ct);
        await using var enlace = await transaccion.VincularAsync(ct);
        var corrida = await ConfirmarInternoAsync(periodo, true, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return corrida;
    }
    public async Task<CorridaDepreciacion> ConfirmarInternoAsync(DateOnly periodo, bool verificarAlcance, CancellationToken ct)
    {
        periodo = CalculoDepreciacion.Mes(periodo);
        var existente = await db.Set<CorridaDepreciacion>().Include(x => x.Detalles).SingleOrDefaultAsync(x => x.Periodo == periodo && !x.Anulada, ct);
        if (existente != null)
        {
            if (verificarAlcance) foreach (var sucursal in existente.Detalles.Select(x => x.SucursalId).Distinct()) await acceso.SucursalAsync(sucursal, true, ct);
            return existente;
        }
        await PeriodoAsync(periodo, ct);
        if (await db.Set<CorridaDepreciacion>().AnyAsync(x => !x.Anulada && x.Periodo > periodo, ct))
            throw new BusinessRuleException("AF_ORDEN_PERIODOS", "Anule las corridas posteriores antes de recalcular este periodo.");
        var ultima = await db.Set<CorridaDepreciacion>().Where(x => !x.Anulada).MaxAsync(x => (DateOnly?)x.Periodo, ct);
        if (ultima != null && ultima.Value.AddMonths(1) < periodo)
            throw new BusinessRuleException("AF_MESES_PENDIENTES", "Ejecute primero los meses intermedios pendientes.");
        if (ultima == null)
        {
            var primero = await PrimerMesAsync(ct);
            if (primero < periodo)
                throw new BusinessRuleException("AF_MESES_PENDIENTES", "Ejecute primero el primer mes pendiente del libro.");
        }
        var filas = await PreviaInternaAsync(periodo, verificarAlcance, ct);
        var revision = (await db.Set<CorridaDepreciacion>().Where(x => x.Periodo == periodo).MaxAsync(x => (int?)x.Revision, ct) ?? 0) + 1;
        var corrida = new CorridaDepreciacion { EmpresaId = Empresa, Periodo = periodo, Revision = revision, Detalles = filas.ToList() };
        foreach (var f in corrida.Detalles) f.CorridaId = corrida.Id;
        var partidas = new List<PartidaRequest>();
        var clases = await db.Clases.ToDictionaryAsync(x => x.Id, ct);
        foreach (var grupo in filas.Where(x => x.Importe > 0).GroupBy(x => new { x.ClaseId, x.SucursalId, x.CentroCostoId, x.EquipoId }))
        {
            var c = clases[grupo.Key.ClaseId];
            if (c.CuentaGastoId == c.CuentaDepreciacionId) throw new BusinessRuleException("AF_CUENTAS_IGUALES", "Gasto y depreciación acumulada deben usar cuentas distintas.");
            var importe = grupo.Sum(x => x.Importe);
            partidas.Add(await PartidaAsync(c.CuentaGastoId, importe, 0, grupo.Key.SucursalId, grupo.Key.EquipoId ?? grupo.Key.CentroCostoId, "Depreciación mensual", ct));
            partidas.Add(await PartidaAsync(c.CuentaDepreciacionId, 0, importe, grupo.Key.SucursalId, grupo.Key.EquipoId ?? grupo.Key.CentroCostoId, "Depreciación acumulada", ct));
        }
        if (partidas.Count > 0)
            corrida.PolizaId = await RegistrarAsync(corrida.Id, "Depreciacion", CalculoDepreciacion.Cierre(periodo),
                $"AF-DEP-{Empresa}-{periodo:yyyyMM}" + (revision == 1 ? "" : $"-R{revision}"), partidas, ct);
        foreach (var f in filas.Where(x => x.Importe > 0)) (await db.Activos.SingleAsync(x => x.Id == f.ActivoId, ct)).Depreciar(f.Importe);
        db.Add(corrida); await db.SaveChangesAsync(ct); return corrida;
    }
    private async Task<IReadOnlyList<DetalleDepreciacion>> PreviaInternaAsync(DateOnly periodo, bool alcance, CancellationToken ct, bool escribir = true)
    {
        var activos = await db.Activos.Include(x => x.Historial).Include(x => x.Traslados).AsSplitQuery().Where(x => x.Estado == EstadoActivo.Activo).ToListAsync(ct);
        var clases = await db.Clases.ToDictionaryAsync(x => x.Id, ct);
        var ajustes = await db.Set<OperacionLibro>().Where(x => x.Tipo == "Revalorizacion" && x.PolizaId != null && !x.Anulada && x.Fecha < CalculoDepreciacion.Mes(periodo)).ToListAsync(ct);
        var mismoMes = await MismoMesAsync(ct);
        var filas = activos.Select(a => CalculoDepreciacion.Calcular(a, clases[a.ClaseId], periodo, ajustes.Where(x => x.ActivoId == a.Id).Sum(x => x.Importe), mismoMes)).ToList();
        if (alcance) foreach (var sucursal in filas.Select(x => x.SucursalId).Distinct()) await acceso.SucursalAsync(sucursal, escribir, ct);
        if (catalogos != null) foreach (var f in filas)
        {
            var descripcion = await catalogos.DescribirAsync(new(Guid.Empty, f.SucursalId, "", f.CentroCostoId, f.EquipoId), ct);
            f.Sucursal = descripcion.Sucursal ?? "Sin sucursal"; f.CentroCosto = descripcion.CentroCosto ?? "Sin centro"; f.Equipo = descripcion.Equipo;
        }
        return filas;
    }
    public async Task<CorridaDepreciacion> AnularAsync(Guid id, int version, string motivo, CancellationToken ct)
    {
        await acceso.PermisoAsync("activos_fijos.depreciacion.anular", ct);
        if (string.IsNullOrWhiteSpace(motivo)) throw new BusinessRuleException("AF_MOTIVO_REQUERIDO", "Capture motivo de anulación.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await BloquearAsync(ct);
        await using var enlace = await transaccion.VincularAsync(ct);
        var c = await db.Set<CorridaDepreciacion>().Include(x => x.Detalles).SingleAsync(x => x.Id == id, ct);
        foreach (var sucursal in c.Detalles.Select(x => x.SucursalId).Distinct()) await acceso.SucursalAsync(sucursal, true, ct);
        if (c.Version != version) throw new ConcurrencyException("Corrida", id);
        if (c.Anulada) return c;
        await PeriodoAsync(c.Periodo, ct);
        if (await db.Set<CorridaDepreciacion>().AnyAsync(x => !x.Anulada && x.Periodo > c.Periodo, ct)
            || await db.Set<OperacionLibro>().AnyAsync(x => !x.Anulada && x.PolizaId != null && x.Fecha >= c.Periodo, ct))
            throw new BusinessRuleException("AF_MOVIMIENTOS_POSTERIORES", "Hay corridas, ajustes o bajas posteriores que dependen de esta depreciación.");
        if (c.PolizaId != null) await polizas.AnularPorDocumentoOrigenAsync("ActivosFijos", "Depreciacion", c.Id, motivo, ct);
        foreach (var f in c.Detalles.Where(x => x.Importe != 0)) (await db.Activos.SingleAsync(x => x.Id == f.ActivoId, ct)).Depreciar(-f.Importe);
        c.Anulada = true; c.MotivoAnulacion = motivo;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return c;
    }
    public async Task<PartidaRequest> PartidaAsync(Guid? cuenta, decimal debe, decimal haber, Guid sucursal, Guid centro, string concepto, CancellationToken ct)
    {
        if (cuenta == null || cuenta == Guid.Empty) throw new BusinessRuleException("AF_CUENTAS_PENDIENTES", "Configure las cuentas contables del libro.");
        var nodos = await centros.ObtenerAsync([centro], ct);
        if (!nodos.TryGetValue(centro, out var n) || !n.ActivoEnCadena) throw new BusinessRuleException("AF_CENTRO_INVALIDO", "El centro de costo no está activo.");
        return new(cuenta.Value, concepto, debe, haber, sucursal, Dim1Id: n.Dim1Id, Dim2Id: n.Dim2Id, Dim3Id: n.Nivel == DimensionContable.Dim3 ? n.Id : null);
    }
    public async Task<Guid> RegistrarAsync(Guid id, string tipo, DateOnly fecha, string llave, IReadOnlyList<PartidaRequest> partidas, CancellationToken ct) =>
        (await polizas.RegistrarAutomaticaAsync(new("AF", fecha, tipo, new DocumentoOrigenPoliza("ActivosFijos", tipo, id, null, null), llave, partidas, OrigenMovimiento.AuxiliarCxP), ct)).Id;
}
