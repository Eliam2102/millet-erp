using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.Reportes.EstadoCuenta;

/// <summary>Pago que impide emitir el estado de cuenta (CA7.14).</summary>
/// <param name="Motivo">"SinRep" (pago confirmado sin REP) o "RepSinReflejar" (REP emitido aún no proyectado en cartera).</param>
public sealed record PagoSinReflejarDto(
    Guid Id,
    DateOnly FechaValor,
    DateOnly FechaLimite,
    decimal Monto,
    string Moneda,
    string? Referencia,
    string Motivo);

public sealed record PendientesEstadoCuentaResponse(
    bool Emitible,
    IReadOnlyList<PagoSinReflejarDto> Pendientes,
    IReadOnlyList<Guid> RecibosSinReflejar,
    string? EmailCliente = null);

/// <summary>
/// Regla única de bloqueo del estado de cuenta (CXC-PR6 + C7.8): no se
/// emite ni se envía mientras el cliente tenga pagos confirmados pendientes
/// de REP o REP emitidos cuyo movimiento aún no aparece en
/// <c>MovimientoCartera</c>. La usan la consulta del reporte, la consulta de
/// pendientes y el comando de envío para que la regla no diverja.
/// </summary>
public sealed class EstadoCuentaBloqueo(CuentasPorCobrarDbContext db, IReppPendientesReadPort? repp)
{
    public const string Codigo = "ESTADO_CUENTA_REPP_PENDIENTE";

    public async Task<PendientesEstadoCuentaResponse> EvaluarAsync(Guid clienteId, CancellationToken cancellationToken)
    {
        if (repp is null) return new(true, [], []);

        var estado = await repp.ConsultarAsync(clienteId, cancellationToken);
        var reflejados = await db.MovimientosCartera.AsNoTracking()
            .Where(m => estado.RecibosEmitidos.Contains(m.OrigenComprobanteId))
            .Select(m => m.OrigenComprobanteId).Distinct().ToListAsync(cancellationToken);
        var sinReflejar = estado.RecibosEmitidos.Except(reflejados).ToArray();

        var pendientes = (estado.Pendientes ?? [])
            .Where(p => p.Estado == "Pendiente"
                || (p.ReciboPagoId is Guid recibo && sinReflejar.Contains(recibo)))
            .Select(p => new PagoSinReflejarDto(p.Id, p.FechaValor, p.FechaLimite, p.Monto, p.Moneda,
                p.Referencia, p.Estado == "Pendiente" ? "SinRep" : "RepSinReflejar"))
            .ToArray();

        return new(!estado.TienePendientes && sinReflejar.Length == 0, pendientes, sinReflejar);
    }

    public async Task VerificarEmitibleAsync(Guid clienteId, CancellationToken cancellationToken)
    {
        var evaluacion = await EvaluarAsync(clienteId, cancellationToken);
        if (!evaluacion.Emitible)
            throw new BusinessRuleException(Codigo,
                "Hay pagos confirmados pendientes de REP o de reflejarse en cartera. Completa su revisión antes de emitir el estado de cuenta.");
    }
}
