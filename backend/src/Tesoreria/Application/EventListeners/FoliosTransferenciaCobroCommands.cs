using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Eventos;
using Millet.Tesoreria.Domain.FoliosTransferencia;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.EventListeners;

// ============================================================================
// F6.6: Caja valida el folio de transferencia por el puerto de lectura y lo
// guarda en su cobro; Tesorería lo marca «Usado» al recibir el cobro y lo
// devuelve a «Vigente» si el cobro se cancela. Idempotente por evento.
// ============================================================================

public sealed record MarcarFoliosTransferenciaUsadosCommand(Guid EventoId, CobroMostradorRegistradoPayload Payload) : IRequest;

public sealed record LiberarFoliosTransferenciaCommand(Guid EventoId, CobroMostradorCanceladoPayload Payload) : IRequest;

public sealed class FoliosTransferenciaCobroHandlers :
    IRequestHandler<MarcarFoliosTransferenciaUsadosCommand>,
    IRequestHandler<LiberarFoliosTransferenciaCommand>
{
    private readonly TesoreriaDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<FoliosTransferenciaCobroHandlers> _logger;

    public FoliosTransferenciaCobroHandlers(TesoreriaDbContext db, IClock clock, ILogger<FoliosTransferenciaCobroHandlers> logger)
    {
        _db = db; _clock = clock; _logger = logger;
    }

    public async Task Handle(MarcarFoliosTransferenciaUsadosCommand command, CancellationToken cancellationToken)
    {
        var p = command.Payload;
        var ahora = _clock.UtcNow;
        var folios = p.FormasPago
            .Where(f => !string.IsNullOrWhiteSpace(f.FolioTransferencia))
            .Select(f => f.FolioTransferencia!.Trim().ToUpperInvariant())
            .Distinct()
            .ToArray();
        if (folios.Length > 0)
        {
            var encontrados = await _db.FoliosTransferencia.IgnoreQueryFilters()
                .Where(f => f.EmpresaId == p.EmpresaId && folios.Contains(f.Folio))
                .ToListAsync(cancellationToken);
            foreach (var folio in encontrados)
            {
                if (folio.Estado != EstadoFolioTransferencia.Vigente && folio.CobroMostradorId != p.CobroMostradorId)
                {
                    // Caja ya lo validó; si aquí no está vigente es una carrera o un dato alterado.
                    _logger.LogWarning(
                        "Folio de transferencia {Folio} en estado {Estado} usado por el cobro {CobroId}; se deja como está.",
                        folio.Folio, folio.Estado, p.CobroMostradorId);
                    continue;
                }
                folio.MarcarUsado(p.CobroMostradorId, ahora);
            }
            foreach (var faltante in folios.Except(encontrados.Select(f => f.Folio)))
                _logger.LogWarning("Folio de transferencia {Folio} del cobro {CobroId} no existe en Tesorería.", faltante, p.CobroMostradorId);
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            command.EventoId, CobroMostradorRegistradoPayload.EventType, ahora,
            detalle: $"cobro={p.CobroMostradorId} folios={string.Join(",", folios)}"));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(LiberarFoliosTransferenciaCommand command, CancellationToken cancellationToken)
    {
        var p = command.Payload;
        var usados = await _db.FoliosTransferencia.IgnoreQueryFilters()
            .Where(f => f.EmpresaId == p.EmpresaId && f.CobroMostradorId == p.CobroMostradorId)
            .ToListAsync(cancellationToken);
        foreach (var folio in usados) folio.Liberar(p.CobroMostradorId);

        _db.EventosProcesados.Add(new EventoProcesado(
            command.EventoId, CobroMostradorCanceladoPayload.EventType, _clock.UtcNow,
            detalle: $"cobro={p.CobroMostradorId} liberados={string.Join(",", usados.Select(f => f.Folio))}"));
        await _db.SaveChangesAsync(cancellationToken);
    }
}
