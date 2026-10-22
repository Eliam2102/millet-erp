using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Contabilidad.Application.PublicPorts;
namespace Millet.ActivosFijos.Application;
public sealed record FilaLibro(Guid ActivoId, string Numero, Guid ClaseId, string Clase, Guid SucursalId, Guid CentroCostoId, Guid? EquipoId, decimal Valor, decimal Acumulada, decimal Neto, string? Sucursal, string? CentroCosto, string? Equipo);
public sealed record FilaConciliacion(Guid ClaseId, string Clase, decimal ValorLibro, decimal AcumuladaLibro, decimal? SaldoActivo, decimal? SaldoDepreciacion,
    decimal? DiferenciaValor, decimal? DiferenciaAcumulada, string? Observacion);
public sealed record ReporteConciliacion(IReadOnlyList<FilaConciliacion> Clases, IReadOnlyList<FilaLibro> Activos, IReadOnlyList<CorridaDepreciacion> Corridas, IReadOnlyList<OperacionLibro> Ajustes);
public sealed class ReportesLibroServicio(ActivosFijosDbContext db, ActivoServicio acceso, ISaldosContablesReadPort saldos, ICatalogosActivoReadPort catalogos)
{
    public async Task<IReadOnlyList<FilaLibro>> LibroAsync(DateOnly corte, Guid? sucursal, CancellationToken ct)
    {
        var ids = await acceso.SucursalesAsync(sucursal, ct);
        var activos = await db.Activos.AsNoTracking().Include(x => x.Historial).Include(x => x.Traslados).AsSplitQuery()
            .Where(x => x.Estado != EstadoActivo.PorCapitalizar && x.InicioUso <= corte && (x.CorteCarga == null || x.CorteCarga <= corte)).ToListAsync(ct);
        var clases = await db.Clases.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var corridas = await db.Set<CorridaDepreciacion>().AsNoTracking().Include(x => x.Detalles).Where(x => !x.Anulada && x.Periodo <= corte).ToListAsync(ct);
        var ops = await db.Set<OperacionLibro>().AsNoTracking().Where(x => !x.Anulada && x.PolizaId != null && x.Fecha <= corte).ToListAsync(ct);
        var resultado = new List<FilaLibro>();
        foreach (var a in activos.Where(a => !ops.Any(o => o.ActivoId == a.Id && (o.Tipo == "Baja" || o.Tipo == "Venta"))))
        {
            var custodia = CalculoDepreciacion.CustodiaAFecha(a, corte);
            if (ids != null && !ids.Contains(custodia.SucursalId)) continue;
            var ajustes = ops.Where(o => o.ActivoId == a.Id);
            var valor = a.ValorOriginal + a.Historial.Where(h => h.Fecha <= corte).Sum(h => h.Importe) + ajustes.Where(o => o.Tipo == "Revalorizacion").Sum(o => o.Importe);
            var acumulada = a.AcumuladaCarga + corridas.Where(c => CalculoDepreciacion.Cierre(c.Periodo) <= corte).SelectMany(c => c.Detalles).Where(d => d.ActivoId == a.Id).Sum(d => d.Importe)
                + ajustes.Where(o => o.Tipo == "Amortizacion").Sum(o => o.Importe);
            var descripcion = await catalogos.DescribirAsync(custodia, ct);
            resultado.Add(new(a.Id, a.Numero, a.ClaseId, clases[a.ClaseId].Clave, custodia.SucursalId, custodia.CentroCostoId, custodia.EquipoId, valor, acumulada, valor - acumulada, descripcion.Sucursal, descripcion.CentroCosto, descripcion.Equipo));
        }
        return resultado;
    }
    public async Task<ReporteConciliacion> ConciliarAsync(DateOnly periodo, Guid? sucursal, CancellationToken ct)
    {
        await acceso.PermisoAsync("activos_fijos.conciliacion.ver", ct);
        var corte = CalculoDepreciacion.Cierre(periodo); var inicio = CalculoDepreciacion.Mes(periodo);
        var ids = await acceso.SucursalesAsync(sucursal, ct);
        var libro = await LibroAsync(corte, sucursal, ct);
        var clases = await db.Clases.AsNoTracking().ToListAsync(ct);
        var cuentas = clases.SelectMany(c => new[] { c.CuentaActivoId, c.CuentaDepreciacionId }).OfType<Guid>().Distinct().ToList();
        var saldo = await saldos.ObtenerAsync(corte, cuentas, ids, ct);
        var filas = clases.Select(c =>
        {
            var valor = libro.Where(a => a.ClaseId == c.Id).Sum(a => a.Valor); var acumulada = libro.Where(a => a.ClaseId == c.Id).Sum(a => a.Acumulada);
            var compartida = clases.Any(otra => otra.Id != c.Id && ((c.CuentaActivoId != null && otra.CuentaActivoId == c.CuentaActivoId) || (c.CuentaDepreciacionId != null && otra.CuentaDepreciacionId == c.CuentaDepreciacionId)));
            decimal? activo = c.CuentaActivoId is Guid ca && !compartida ? saldo.GetValueOrDefault(ca) : null;
            decimal? dep = c.CuentaDepreciacionId is Guid cd && !compartida ? -saldo.GetValueOrDefault(cd) : null;
            return new FilaConciliacion(c.Id, c.Clave, valor, acumulada, activo, dep, valor - activo, acumulada - dep,
                compartida ? "Cuenta compartida entre clases: no es posible atribuir el saldo por clase." : activo == null || dep == null ? "Configure las cuentas de la clase." : null);
        }).ToList();
        var corridas = await db.Set<CorridaDepreciacion>().AsNoTracking().Include(c => c.Detalles.Where(d => ids == null || ids.Contains(d.SucursalId))).Where(c => c.Periodo == inicio).ToListAsync(ct);
        var operaciones = await (from o in db.Set<OperacionLibro>().AsNoTracking() join a in db.Activos on o.ActivoId equals a.Id
            where o.Fecha >= inicio && o.Fecha <= corte && (ids == null || ids.Contains(a.SucursalId)) select o).ToListAsync(ct);
        return new(filas, libro, corridas, operaciones);
    }
}
