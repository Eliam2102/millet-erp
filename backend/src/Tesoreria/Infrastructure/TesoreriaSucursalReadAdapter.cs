using Microsoft.EntityFrameworkCore;
using Millet.Tesoreria.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
namespace Millet.Tesoreria.Infrastructure;
public sealed class TesoreriaSucursalReadAdapter(TesoreriaDbContext db, ICxpSucursalReadPort cxp,
    ICxcSucursalReadPort cxc, IFacturacionSucursalReadPort facturacion) : ITesoreriaSucursalReadPort
{
    public async Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct)
    {
        var facturas = (await cxp.ListarAsync("factura_proveedor", ct)).ToDictionary(x => x.Id, x => x.Sucursales);
        if (tipo == "pasivo")
        {
            var pasivos = await db.PasivosPendientesPago.AsNoTracking().Select(x => new { x.Id, x.FacturaProveedorId, x.OrigenTipo, x.OrigenId }).ToListAsync(ct);
            var reposiciones = (await cxp.ListarAsync("reposicion_caja", ct)).ToDictionary(x => x.Id, x => x.Sucursales);
            return pasivos.Select(x => new DocumentoSucursales(x.Id,
                facturas.GetValueOrDefault(x.FacturaProveedorId) ?? reposiciones.GetValueOrDefault(x.OrigenId) ?? Array.Empty<Guid>())).ToArray();
        }
        var aplicaciones = await db.AplicacionesPagoProveedor.AsNoTracking().Select(x => new { x.Id, x.MovimientoId, x.FacturaProveedorId }).ToListAsync(ct);
        if (tipo == "pago_proveedor") return aplicaciones.Select(x => new DocumentoSucursales(x.Id,
            facturas.GetValueOrDefault(x.FacturaProveedorId) ?? Array.Empty<Guid>())).ToArray();
        var propuestas = (await cxc.ListarAsync("propuesta_cxc", ct)).ToDictionary(x => x.Id, x => x.Sucursales);
        var sesiones = (await facturacion.ListarAsync("sesion_caja", ct)).ToDictionary(x => x.Id, x => x.Sucursales);
        var depositos = await db.DepositosConfirmacion.AsNoTracking()
            .Select(x => new { x.Id, x.MovimientoId, x.PropuestaCxcId, x.CajaSesionId }).ToListAsync(ct);
        IReadOnlyList<Guid> SucursalesDeposito(Guid? propuestaId, Guid? cajaId) =>
            propuestaId is Guid p ? propuestas.GetValueOrDefault(p) ?? Array.Empty<Guid>() :
            cajaId is Guid c ? sesiones.GetValueOrDefault(c) ?? Array.Empty<Guid>() : Array.Empty<Guid>();
        if (tipo == "deposito") return depositos.Select(x => new DocumentoSucursales(x.Id,
            SucursalesDeposito(x.PropuestaCxcId, x.CajaSesionId))).ToArray();
        if (tipo is "movimiento_bancario" or "cuenta_bancaria")
        {
            var movimientos = await db.MovimientosBancarios.AsNoTracking().Select(x => new { x.Id, x.ContramovimientoDe, x.CuentaBancariaId }).ToListAsync(ct);
            var alcances = movimientos.Select(x => {
                var origenes = aplicaciones.Where(a => a.MovimientoId == x.Id || a.MovimientoId == x.ContramovimientoDe)
                    .Select(a => facturas.GetValueOrDefault(a.FacturaProveedorId) ?? Array.Empty<Guid>())
                    .Concat(depositos.Where(d => d.MovimientoId == x.Id || (x.ContramovimientoDe != null && d.MovimientoId == x.ContramovimientoDe))
                        .Select(d => SucursalesDeposito(d.PropuestaCxcId, d.CajaSesionId))).ToArray();
                return new DocumentoSucursales(x.Id, origenes.Length == 0 || origenes.Any(o => o.Count == 0)
                    ? Array.Empty<Guid>() : origenes.SelectMany(o => o).Distinct().ToArray());
            }).ToArray();
            if (tipo == "movimiento_bancario") return alcances;
            // Los reportes P5 incluyen el saldo de la cuenta completa. Exigen acceso
            // a todos sus movimientos; un origen indeterminado requiere corporativo.
            var cuentas = await db.CuentasBancarias.AsNoTracking().Select(c => c.Id).ToListAsync(ct);
            var porMovimiento = alcances.ToDictionary(x => x.Id, x => x.Sucursales);
            return cuentas.Select(id => {
                var origenes = movimientos.Where(m => m.CuentaBancariaId == id).Select(m => porMovimiento[m.Id]).ToArray();
                return new DocumentoSucursales(id, origenes.Length == 0 || origenes.Any(o => o.Count == 0)
                    ? Array.Empty<Guid>() : origenes.SelectMany(o => o).Distinct().ToArray());
            }).ToArray();
        }
        return Array.Empty<DocumentoSucursales>();
    }
}
