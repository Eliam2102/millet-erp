using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Eventos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.EventListeners;

// ============================================================================
// TES-PR7 (§3.3 paso 4): al timbrarse el REPP, la confirmación de depósito
// que lo originó queda fiscalmente cubierta (repp_timbrado=true).
//
// La bandeja manual publica el movimiento de origen: conserva la correlación
// aunque Facturación corrija las facturas. Eventos anteriores usan el desglose.
// ============================================================================

public sealed record MarcarReppTimbradoCommand(
    Guid EventoId,
    ReciboPagoTimbradoPayload Payload) : IRequest;

public sealed class MarcarReppTimbradoHandler : IRequestHandler<MarcarReppTimbradoCommand>
{
    public const string EventType = ReciboPagoTimbradoPayload.EventType;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly TesoreriaDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<MarcarReppTimbradoHandler> _logger;

    public MarcarReppTimbradoHandler(
        TesoreriaDbContext db, IClock clock, ILogger<MarcarReppTimbradoHandler> logger)
    {
        _db = db; _clock = clock; _logger = logger;
    }

    public async Task Handle(MarcarReppTimbradoCommand command, CancellationToken cancellationToken)
    {
        var p = command.Payload;
        var ahora = _clock.UtcNow;

        // Volumen trivial (70-100 REPP/semana, confirmaciones sin timbrar
        // acotadas): candidatos a memoria y comparación del desglose ahí.
        var candidatas = await _db.DepositosConfirmacion
            .Where(d => d.Estado == EstadoDepositoConfirmacion.Confirmada
                        && !d.ReppTimbrado
                        && d.PropuestaCxcId != null)
            .OrderBy(d => d.ResueltaEn)
            .ToListAsync(cancellationToken);

        var objetivo = p.FacturasPagadas
            .Select(f => (f.FacturaVentaId, f.ImportePagado))
            .OrderBy(x => x.FacturaVentaId)
            .ToList();

        var matches = p.MovimientoBancarioId is Guid movimientoId
            ? candidatas.Where(d => d.MovimientoId == movimientoId).ToList()
            : candidatas.Where(d => DesgloseCoincide(d.FacturasJson, objetivo)).ToList();

        if (matches.Count > 0)
        {
            if (matches.Count > 1)
            {
                _logger.LogWarning(
                    "REPP timbrado {ReciboPagoId} coincide con {Count} confirmaciones; se marca la más antigua {DepositoId}.",
                    p.ReciboPagoId, matches.Count, matches[0].Id);
            }
            matches[0].MarcarReppTimbrado();
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            command.EventoId, EventType, ahora,
            detalle: $"repp={p.ReciboPagoId} uuid={p.Uuid} match={(matches.Count > 0 ? matches[0].Id.ToString() : "ninguno")}"));

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static bool DesgloseCoincide(
        string facturasJson, List<(Guid FacturaVentaId, decimal ImportePagado)> objetivo)
    {
        List<PropuestaFacturaPayload>? facturas;
        try
        {
            facturas = JsonSerializer.Deserialize<List<PropuestaFacturaPayload>>(facturasJson, JsonOpts);
        }
        catch (JsonException)
        {
            return false;
        }
        if (facturas is null || facturas.Count != objetivo.Count) return false;

        var propio = facturas
            .Select(f => (f.FacturaVentaId, f.ImporteAplicado))
            .OrderBy(x => x.FacturaVentaId)
            .ToList();

        return propio.SequenceEqual(objetivo);
    }
}
