using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Application.AplicacionPagos;

/// <summary>
/// C7.4 · parcialidades (ficha 26-oct): la «parcialidad siguiente» y el saldo insoluto que
/// muestra CxC salen de los mismos datos que Facturación usa para el complemento de pago
/// (<c>FacturasCobrablesPpdQuery.NumParcialidadSiguiente</c>, vía
/// <see cref="IReppPendientesReadPort.ParcialidadesAsync"/>). Facturación sigue siendo la
/// fuente del número que va al REP; CxC solo lo muestra.
///
/// <para>
/// Decisión: las propuestas abiertas de CxC (Propuesta, o Confirmada sin REP timbrado)
/// SÍ suman a la sugerencia — cada una será un REP antes que la nueva — y su importe se
/// descuenta del disponible. Si Facturación no conoce la factura (no PPD o sin timbrar),
/// se usa la proyección de cartera de CxC (pagos vigentes + 1).
/// </para>
/// </summary>
internal static class ParcialidadesCxc
{
    public sealed record Resultado(decimal Saldo, decimal Reservado, int ReservasAbiertas, int? NumParcialidadSiguiente)
    {
        public decimal Disponible => Saldo - Reservado;
    }

    public static async Task<IReadOnlyDictionary<Guid, Resultado>> CalcularAsync(
        CuentasPorCobrarDbContext db, IReppPendientesReadPort? facturacion,
        IReadOnlyCollection<FacturaCartera> facturas, CancellationToken ct)
    {
        if (facturas.Count == 0) return new Dictionary<Guid, Resultado>();
        var ids = facturas.Select(f => f.Id).ToList();
        var reservas = (await db.PropuestasAplicacionPago.AsNoTracking()
                .Where(p => p.Estado == EstadoPropuestaAplicacion.Propuesta ||
                    (p.Estado == EstadoPropuestaAplicacion.Confirmada && !p.ReppTimbrado))
                .SelectMany(p => p.Facturas)
                .Where(f => ids.Contains(f.FacturaCarteraId))
                .Select(f => new { f.FacturaCarteraId, f.ImporteAplicado })
                .ToListAsync(ct))
            .GroupBy(f => f.FacturaCarteraId)
            .ToDictionary(g => g.Key, g => (Importe: g.Sum(x => x.ImporteAplicado), Lineas: g.Count()));
        var pagosCartera = (await db.MovimientosCartera.AsNoTracking()
                .Where(m => ids.Contains(m.FacturaCarteraId) && m.Tipo == TipoMovimientoCartera.Pago && !m.Revertido)
                .Select(m => m.FacturaCarteraId)
                .ToListAsync(ct))
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var deFacturacion = facturacion is null
            ? new Dictionary<Guid, ParcialidadPpd>()
            : await facturacion.ParcialidadesAsync(facturas.Select(f => f.FacturaVentaId).ToList(), ct);

        return facturas.ToDictionary(f => f.Id, f =>
        {
            var esPpd = string.Equals(f.MetodoPago, "PPD", StringComparison.OrdinalIgnoreCase);
            var (importe, lineas) = reservas.GetValueOrDefault(f.Id);
            if (esPpd && deFacturacion.TryGetValue(f.FacturaVentaId, out var fac))
                return new Resultado(fac.Saldo, importe, lineas, fac.NumParcialidadSiguiente + lineas);
            return new Resultado(f.SaldoPendiente, importe, lineas,
                esPpd ? pagosCartera.GetValueOrDefault(f.Id) + lineas + 1 : null);
        });
    }
}
