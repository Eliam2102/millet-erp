using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
namespace Millet.CuentasPorCobrar.Infrastructure;
public sealed class CxcSucursalReadAdapter(CuentasPorCobrarDbContext db, IFacturacionSucursalReadPort facturacion) : ICxcSucursalReadPort
{
    public async Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct)
    {
        var origenes = (await facturacion.ListarAsync("comprobante", ct)).ToDictionary(x => x.Id, x => x.Sucursales);
        var facturas = await db.FacturasCartera.AsNoTracking().Select(x => new { x.Id, x.FacturaVentaId, x.ClienteId }).ToListAsync(ct);
        var mapa = facturas.ToDictionary(x => x.Id, x => origenes.GetValueOrDefault(x.FacturaVentaId) ?? Array.Empty<Guid>());
        if (tipo == "factura_cartera") return mapa.Select(x => new DocumentoSucursales(x.Key, x.Value)).ToArray();
        if (tipo == "propuesta_cxc")
        {
            var propuestas = await db.PropuestasAplicacionPago.AsNoTracking().Include(x => x.Facturas).ToListAsync(ct);
            return propuestas.Select(x => new DocumentoSucursales(x.Id,
                x.Facturas.Count == 0 || x.Facturas.Any(f => !mapa.TryGetValue(f.FacturaCarteraId, out var ids) || ids.Count == 0)
                ? Array.Empty<Guid>() : x.Facturas.SelectMany(f => mapa[f.FacturaCarteraId]).Distinct().ToArray())).ToArray();
        }
        var clientes = facturas.Where(x => x.ClienteId != null).GroupBy(x => x.ClienteId!.Value).ToDictionary(x => x.Key,
            x => x.Any(f => mapa[f.Id].Count == 0) ? (IReadOnlyList<Guid>)Array.Empty<Guid>() :
                x.SelectMany(f => mapa[f.Id]).Distinct().ToArray());
        if (tipo == "cliente_cartera") return clientes.Select(x => new DocumentoSucursales(x.Key, x.Value)).ToArray();
        var filas = tipo == "seguimiento_cobranza"
            ? await db.SeguimientosCobranza.AsNoTracking().Select(x => new { x.Id, x.ClienteId }).ToListAsync(ct)
            : await db.AlertasCartera.AsNoTracking().Select(x => new { x.Id, x.ClienteId }).ToListAsync(ct);
        // La gestión global del cliente exige todas las sucursales de su cartera.
        return filas.Select(x => new DocumentoSucursales(x.Id,
            clientes.GetValueOrDefault(x.ClienteId) ?? Array.Empty<Guid>())).ToArray();
    }
}
