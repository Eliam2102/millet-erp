using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.Alertas;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Eventos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.EventListeners;

public sealed record PagoClienteConfirmadoPayload(Guid EmpresaId, DateTimeOffset OcurridoEn,
    Guid? PropuestaId, Guid ClienteId, Guid MovimientoBancarioId, Guid? ConfirmadaPor);
public sealed record PropuestaRechazadaPayload(Guid EmpresaId, DateTimeOffset OcurridoEn,
    Guid PropuestaId, Guid? ClienteId, string Motivo, Guid RechazadaPor);
public sealed record ResolverPropuestaTesoreriaCommand(Guid EventId, Guid EmpresaId,
    Guid PropuestaId, Guid UsuarioId, DateTimeOffset OcurridoEn,
    Guid? MovimientoBancarioId, string? MotivoRechazo) : IRequest;

public sealed class ResolverPropuestaTesoreriaHandler(CuentasPorCobrarDbContext db, IClock clock,
    ILogger<ResolverPropuestaTesoreriaHandler>? logger = null, IOptions<AlertasCarteraOptions>? alertas = null)
    : IRequestHandler<ResolverPropuestaTesoreriaCommand>
{
    public const string ConfirmadaEventType = "tesoreria.pago-cliente.confirmado.v1";
    public const string RechazadaEventType = "tesoreria.propuesta-aplicacion.rechazada.v1";

    public async Task Handle(ResolverPropuestaTesoreriaCommand command, CancellationToken cancellationToken)
    {
        var tipo = command.MovimientoBancarioId is null ? RechazadaEventType : ConfirmadaEventType;
        if (await db.EventosProcesados.AnyAsync(e => e.EventoId == command.EventId && e.EventoTipo == tipo, cancellationToken)) return;
        var propuesta = await db.PropuestasAplicacionPago.Include(p => p.Facturas).FirstOrDefaultAsync(
            p => p.Id == command.PropuestaId && p.EmpresaId == command.EmpresaId, cancellationToken)
            ?? throw new EntityNotFoundException("PAP_NO_ENCONTRADA", "La propuesta de CxC todavía no está disponible; se reintentará.");
        if (command.UsuarioId == Guid.Empty)
            throw new BusinessRuleException("PAP_USUARIO_NO_IDENTIFICADO", "El evento no identifica a quien resolvió la propuesta.");
        if (propuesta.Estado == EstadoPropuestaAplicacion.Propuesta)
        {
            if (command.MovimientoBancarioId is Guid movimientoId)
                propuesta.Confirmar(command.UsuarioId, command.OcurridoEn, movimientoId);
            else
                propuesta.Rechazar(command.UsuarioId, command.MotivoRechazo ?? "", command.OcurridoEn);
        }
        else if ((command.MovimientoBancarioId is not null &&
                     (propuesta.Estado != EstadoPropuestaAplicacion.Confirmada || propuesta.MovimientoBancarioId != command.MovimientoBancarioId)) ||
                 (command.MovimientoBancarioId is null && propuesta.Estado != EstadoPropuestaAplicacion.Rechazada))
            throw new BusinessRuleException("PAP_RESOLUCION_INCONSISTENTE", "La propuesta ya tiene una resolución distinta.");
        var detalle = $"Propuesta={propuesta.Id}";
        if (propuesta.Estado == EstadoPropuestaAplicacion.Confirmada && !propuesta.ReppTimbrado)
            detalle += await AplicarFacturasPueAsync(propuesta, command.OcurridoEn, cancellationToken);
        db.EventosProcesados.Add(new EventoProcesado(command.EventId, tipo, clock.UtcNow, detalle));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// H-U49: una factura PUE no lleva REP (solo PPD), así que su pago se aplica a cartera al confirmarse el depósito, con origen =
    /// propuesta. Solo hasta el saldo de la factura: si ya se cobró en caja (cobro de mostrador aplicado a cartera) no se abona dos
    /// veces. Si todas las facturas son PUE la propuesta queda cerrada. La póliza del cobro la genera Contabilidad con la
    /// confirmación de Tesorería (una vez); aquí no se publica nada.
    /// </summary>
    private async Task<string> AplicarFacturasPueAsync(PropuestaAplicacionPago propuesta, DateTimeOffset ocurridoEn, CancellationToken ct)
    {
        var ids = propuesta.Facturas.Select(f => f.FacturaCarteraId).Distinct().ToList();
        var cartera = await db.FacturasCartera.Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var pue = propuesta.Facturas
            .Where(l => cartera.TryGetValue(l.FacturaCarteraId, out var f) && !string.Equals(f.MetodoPago, "PPD", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (pue.Count == 0) return "";

        var yaAplicadas = await db.MovimientosCartera
            .Where(m => m.OrigenComprobanteId == propuesta.Id && m.Tipo == TipoMovimientoCartera.Pago)
            .Select(m => m.FacturaCarteraId).ToListAsync(ct);
        var notas = new List<string>();
        var pagados = new HashSet<(Guid?, string)>();
        foreach (var linea in pue)
        {
            var factura = cartera[linea.FacturaCarteraId];
            if (yaAplicadas.Contains(factura.Id)) continue;
            var importe = Math.Min(linea.ImporteAplicado, factura.SaldoPendiente);
            if (importe <= 0m)
            {
                notas.Add($"{factura.Folio} sin saldo (¿cobrada en caja?), no se aplica");
                logger?.LogWarning("[H-U49] Factura PUE {Folio} sin saldo al confirmar la propuesta {PropuestaId}: no se aplica de nuevo.",
                    factura.Folio, propuesta.Id);
                continue;
            }
            factura.AplicarPago(importe);
            db.MovimientosCartera.Add(new MovimientoCartera(propuesta.EmpresaId, factura.Id, TipoMovimientoCartera.Pago,
                propuesta.Id, importe, ocurridoEn));
            pagados.Add((factura.ClienteId, factura.Moneda));
            notas.Add($"{factura.Folio} PUE aplicada {importe}");
        }
        if (pue.Count == propuesta.Facturas.Count)
            propuesta.MarcarAplicadaSinRep();

        var tolerancia = Math.Max(0, (alertas?.Value ?? new AlertasCarteraOptions()).ToleranciaDiasVencido);
        foreach (var (clienteId, moneda) in pagados)
            await BloqueoPorVencimiento.ReevaluarTrasPagoAsync(db, clienteId, moneda, tolerancia,
                DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), (ILogger?)logger ?? NullLogger.Instance, ct);
        return "; " + string.Join("; ", notas);
    }
}
