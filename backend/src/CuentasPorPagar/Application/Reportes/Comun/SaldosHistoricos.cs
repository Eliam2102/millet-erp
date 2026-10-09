using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Administracion.Application.Abstractions;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.CuentasPorPagar.Application.Reportes.Comun;

public sealed record SaldoHistorico(Factura Factura, decimal Saldo, bool EnRevision, string Nombre, string Rfc);

public sealed class SaldosHistoricos(CuentasPorPagarDbContext db, IProveedorReadPort proveedores,
    ICurrentUserContext usuario, ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales)
{
    public static DateTimeOffset FinExclusivo(DateOnly corte) => new(corte.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    public async Task<IReadOnlyList<SaldoHistorico>> LeerAsync(DateOnly corte, Guid? proveedorId, Guid? sucursalId,
        string? obra, CancellationToken cancellationToken)
    {
        var fin = FinExclusivo(corte);
        var q = db.FacturasProveedor.AsNoTracking().Include(f => f.Movimientos).Include(f => f.Bitacora)
            .Where(f => f.FechaDocumento < fin && (f.FechaCancelacion == null || f.FechaCancelacion >= fin));
        if (proveedorId is Guid p) q = q.Where(f => f.ProveedorId == p);
        if (sucursalId is Guid s)
        {
            await SucursalScopeGuard.VerificarAsync(usuario.UserId, PermisosCanonicos.CuentasPorPagarReportesLeerTodasSucursales,
                permisos, (u, token) => sucursales.EstaAsociadoAsync(u, s, token), cancellationToken);
            q = q.Where(f => f.SucursalId == s);
        }
        else if (!await permisos.TieneAsync(PermisosCanonicos.CuentasPorPagarReportesLeerTodasSucursales, cancellationToken))
        {
            var ids = usuario.UserId is Guid u ? await sucursales.ListarIdsAsync(u, cancellationToken) : [];
            q = q.Where(f => ids.Contains(f.SucursalId));
        }
        if (!string.IsNullOrWhiteSpace(obra)) q = q.Where(f => f.Obra == obra.Trim());
        var facturas = await q.ToListAsync(cancellationToken);
        var resultado = new List<SaldoHistorico>();
        var maestro = new Dictionary<Guid, ProveedorDto?>();
        foreach (var f in facturas)
        {
            VerificarHistorial(f);
            var saldo = f.Total - f.Movimientos.Where(m => m.Fecha <= corte).Sum(m => m.Monto);
            if (saldo <= 0) continue;
            if (!maestro.TryGetValue(f.ProveedorId, out var proveedor))
                maestro[f.ProveedorId] = proveedor = await proveedores.ObtenerAsync(f.ProveedorId, cancellationToken);
            var estado = f.Bitacora.Where(b => b.OcurridoEn < fin).OrderBy(b => b.OcurridoEn).ThenBy(b => b.Id)
                .LastOrDefault()?.EstadoNuevo;
            resultado.Add(new(f, saldo, estado == EstadoPasivo.EnRevision,
                proveedor?.RazonSocial ?? "[PROVEEDOR POR CONFIRMAR]", proveedor?.Rfc ?? "[RFC POR CONFIRMAR]"));
        }
        return resultado;
    }

    public static void VerificarHistorial(Factura f)
    {
        if (f.Movimientos.Where(m => m.Tipo == TipoMovimientoPasivo.Pago).Sum(m => m.Monto) != f.ImportePagado ||
            f.Movimientos.Where(m => m.Tipo == TipoMovimientoPasivo.Anticipo).Sum(m => m.Monto) != f.AnticipoAplicadoTotal ||
            f.Movimientos.Where(m => m.Tipo is TipoMovimientoPasivo.NotaCredito or TipoMovimientoPasivo.NotaCargo).Sum(m => m.Monto) != f.NcAplicadasTotal)
            throw new BusinessRuleException("CXP_HISTORICO_POR_CONFIRMAR",
                "Por confirmar: hay aplicaciones anteriores a P8 sin fecha reconstruible. Completa el historial antes de usar el auxiliar para conciliar con Contabilidad.");
    }
}

public static class ReporteHistoricoBuilder
{
    public static readonly string[] Buckets = ["por_vencer", "b0_30", "b31_60", "b61_90", "bMas90"];
    public static ColumnaReporte Texto(string key, string label) => new(key, label, TipoColumnaReporte.Texto, AlineacionColumna.Izquierda);
    public static ColumnaReporte Importe(string key, string label) => new(key, label, TipoColumnaReporte.Moneda, AlineacionColumna.Derecha);
    public static IReadOnlyList<ColumnaReporte> Columnas(bool cartera, bool auxiliar = false) =>
        new[] { Texto("proveedor_nombre", "Proveedor"), Texto("rfc", "RFC"), Texto("moneda", "Moneda") }
        .Concat(cartera ? [new ColumnaReporte("en_revision", "En revisión", TipoColumnaReporte.Booleano, AlineacionColumna.Centro)] : [])
        .Concat(auxiliar ? [] : new[] { Importe("por_vencer", "Por vencer"), Importe("b0_30", "0–30 días"),
            Importe("b31_60", "31–60 días"), Importe("b61_90", "61–90 días"), Importe("bMas90", "+90 días") })
        .Concat([Importe("total", "Saldo"), new ColumnaReporte("numero_facturas", "Facturas", TipoColumnaReporte.Entero, AlineacionColumna.Derecha)]).ToArray();

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Agrupar(IEnumerable<SaldoHistorico> saldos, DateOnly corte, bool cartera) =>
        saldos.GroupBy(s => new { s.Factura.ProveedorId, s.Factura.Moneda, Revision = cartera && s.EnRevision })
        .OrderBy(g => g.First().Nombre).ThenBy(g => g.Key.Moneda).ThenBy(g => g.Key.Revision)
        .Select(g =>
        {
            var fila = new Dictionary<string, object?> {
                ["proveedor_id"] = g.Key.ProveedorId, ["proveedor_nombre"] = g.First().Nombre, ["rfc"] = g.First().Rfc,
                ["moneda"] = g.Key.Moneda, ["en_revision"] = g.Key.Revision,
                ["total"] = g.Sum(s => s.Saldo), ["numero_facturas"] = g.Count() };
            foreach (var bucket in Buckets) fila[bucket] = g.Where(s => BucketsAntiguedad.CalcularBucket(s.Factura.FechaVencimiento, corte) == bucket).Sum(s => s.Saldo);
            return (IReadOnlyDictionary<string, object?>)fila;
        }).ToArray();

    public static IReadOnlyDictionary<string, object?> Totales(IEnumerable<IReadOnlyDictionary<string, object?>> filas, params string[] importes) =>
        new Dictionary<string, object?> { ["por_moneda"] = filas.GroupBy(f => (string)f["moneda"]!).OrderBy(g => g.Key)
            .Select(g =>
            {
                var t = new Dictionary<string, object?> { ["moneda"] = g.Key };
                foreach (var key in importes) t[key] = g.Sum(f => Convert.ToDecimal(f.GetValueOrDefault(key) ?? 0));
                return t;
            }).ToArray() };

    public static IReadOnlyList<FiltroAplicado> Filtros(DateOnly corte, Guid? proveedorId, Guid? sucursalId, string? obra = null, string? nombreProveedor = null) =>
        new[] { new FiltroAplicado("Fecha de corte", corte.ToString("yyyy-MM-dd")) }
        .Concat(proveedorId is not null ? [new FiltroAplicado("Proveedor", nombreProveedor ?? "[PROVEEDOR POR CONFIRMAR]")] : [])
        .Concat(sucursalId is Guid s ? [new FiltroAplicado("Sucursal", s.ToString())] : [])
        .Concat(string.IsNullOrWhiteSpace(obra) ? [] : new[] { new FiltroAplicado("Obra", obra) }).ToArray();
}
