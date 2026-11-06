using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.ConciliacionAuxiliares;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;

/// <summary>
/// F1-CON-09: auxiliar de proveedores al corte para la conciliación de Contabilidad. Reutiliza el saldo histórico de los reportes de
/// CxP (<see cref="SaldosHistoricos"/>). Contabilidad es corporativa: no se limita por las sucursales del usuario. Una factura con
/// historial incompleto no tumba la conciliación: entra con su saldo al corte calculado y se cuenta aparte.
/// </summary>
public sealed class AuxiliarProveedoresContabilidadAdapter(CuentasPorPagarDbContext db) : IAuxiliarProveedoresReadPort
{
    public async Task<AuxiliarProveedoresAlCorte> LeerAsync(DateOnly corte, Guid? sucursalId, CancellationToken ct)
    {
        var q = SaldosHistoricos.VigentesAlCorte(db.FacturasProveedor.AsNoTracking().Include(f => f.Movimientos), corte);
        if (sucursalId is Guid s) q = q.Where(f => f.SucursalId == s);
        var facturas = await q.ToListAsync(ct);
        var porConfirmar = 0;
        var documentos = new List<DocumentoProveedorAlCorte>();
        foreach (var f in facturas)
        {
            if (!SaldosHistoricos.HistorialCompleto(f)) porConfirmar++;
            var saldo = SaldosHistoricos.SaldoAlCorte(f, corte);
            if (saldo <= 0) continue;
            var tc = f.Moneda == "MXN" ? 1m : f.TipoCambio ?? 1m;
            var folio = string.IsNullOrWhiteSpace(f.SerieProveedor) ? f.FolioProveedor : $"{f.SerieProveedor}-{f.FolioProveedor}";
            documentos.Add(new(f.Id, f.ProveedorId, f.SucursalId, folio, f.UuidCfdi, f.Moneda, saldo, Math.Round(saldo * tc, 2)));
        }
        return new(documentos, porConfirmar);
    }
}
