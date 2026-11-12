using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Application.Alertas;

/// <summary>
/// Regla del bloqueo automático por vencimiento (C7.5, R7/P7.2): una
/// factura «rebasa» cuando han pasado más de <c>plazo del cliente +
/// tolerancia</c> días desde su timbrado. El plazo es el de la línea de
/// crédito vigente del cliente en esa moneda.
/// </summary>
public static class BloqueoPorVencimiento
{
    /// <summary>Día en que la factura empieza a bloquear (primer día fuera de plazo + tolerancia).</summary>
    public static DateOnly DiaDeBloqueo(DateTimeOffset fechaTimbrado, int plazoDias, int toleranciaDias) =>
        DateOnly.FromDateTime(fechaTimbrado.UtcDateTime).AddDays(plazoDias + toleranciaDias + 1);

    /// <summary>Días transcurridos desde el timbrado.</summary>
    public static int DiasDesdeTimbrado(DateTimeOffset fechaTimbrado, DateOnly hoy) =>
        hoy.DayNumber - DateOnly.FromDateTime(fechaTimbrado.UtcDateTime).DayNumber;

    /// <summary>
    /// Fechas de bloqueo de las facturas que hoy ya rebasan el umbral; vacío
    /// si ninguna lo rebasa.
    /// </summary>
    public static IReadOnlyList<DateOnly> FacturasQueRebasan(
        IEnumerable<DateTimeOffset> fechasTimbrado, int plazoDias, int toleranciaDias, DateOnly hoy) =>
        fechasTimbrado
            .Select(f => DiaDeBloqueo(f, plazoDias, toleranciaDias))
            .Where(d => d <= hoy)
            .ToList();

    /// <summary>
    /// M-237: igual que la versión por fechas, pero una factura con plazo corregido a mano usa ese plazo en lugar del de la línea. Sin
    /// corrección (plazo null) el resultado es idéntico al de siempre.
    /// </summary>
    public static IReadOnlyList<DateOnly> FacturasQueRebasan(
        IEnumerable<(DateTimeOffset FechaTimbrado, int? PlazoCorregidoDias)> facturas,
        int plazoLineaDias, int toleranciaDias, DateOnly hoy) =>
        facturas
            .Select(f => DiaDeBloqueo(f.FechaTimbrado, f.PlazoCorregidoDias ?? plazoLineaDias, toleranciaDias))
            .Where(d => d <= hoy)
            .ToList();

    /// <summary>
    /// ¿Hay que bloquear? Sí, si alguna factura rebasa el umbral y no quedó
    /// cubierta por el último desbloqueo manual autorizado (supuesto C7.5 «a
    /// validar»: el desbloqueo manual cubre lo vencido a esa fecha; otra
    /// factura que rebase después vuelve a bloquear).
    /// </summary>
    public static bool DebeBloquear(IReadOnlyList<DateOnly> diasDeBloqueo, DateTimeOffset? desbloqueoManualEn)
    {
        if (diasDeBloqueo.Count == 0) return false;
        if (desbloqueoManualEn is not DateTimeOffset d) return true;
        var diaDesbloqueo = DateOnly.FromDateTime(d.UtcDateTime);
        return diasDeBloqueo.Any(dia => dia > diaDesbloqueo);
    }

    /// <summary>
    /// Desbloqueo automático al aplicar un pago o NC (C7.5-a): si la línea
    /// del cliente está bloqueada por vencimiento y ya no tiene facturas
    /// que rebasen plazo + tolerancia, se desbloquea. Lee las facturas
    /// rastreadas, así que ve el pago recién aplicado antes del
    /// <c>SaveChanges</c>.
    /// </summary>
    public static async Task<bool> ReevaluarTrasPagoAsync(
        CuentasPorCobrarDbContext db,
        Guid? clienteId,
        string moneda,
        int toleranciaDias,
        DateOnly hoy,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (clienteId is not Guid cliente) return false;

        var linea = await db.LineasCredito
            .FirstOrDefaultAsync(l => l.ClienteId == cliente
                                   && l.Moneda == moneda
                                   && l.Estado == EstadoLineaCredito.Bloqueada
                                   && l.BloqueoAutomatico,
                cancellationToken);
        if (linea is null) return false;

        // Las facturas se consultan rastreadas: EF conserva los valores en
        // memoria de las que ya se modificaron en este handler.
        var facturas = await db.FacturasCartera
            .Where(f => f.ClienteId == cliente
                     && f.Moneda == moneda
                     && (f.Estado == EstadoFacturaCartera.Abierta
                      || f.Estado == EstadoFacturaCartera.Parcial))
            .ToListAsync(cancellationToken);

        var vivas = facturas
            .Where(f => f.Estado is EstadoFacturaCartera.Abierta or EstadoFacturaCartera.Parcial
                     && f.SaldoPendiente > 0)
            .Select(f => (f.FechaTimbrado, f.PlazoCorregidoDias));

        if (FacturasQueRebasan(vivas, linea.PlazoDias, toleranciaDias, hoy).Count > 0) return false;

        linea.DesbloquearPorPago();
        logger.LogInformation(
            "[BloqueoVencimiento] Línea {LineaId} desbloqueada automáticamente: el cliente {ClienteId} ya no tiene facturas fuera de plazo + {Tolerancia} días.",
            linea.Id, cliente, toleranciaDias);
        return true;
    }
}
