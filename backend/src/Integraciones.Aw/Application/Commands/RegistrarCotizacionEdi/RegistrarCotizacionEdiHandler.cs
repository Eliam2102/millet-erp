using System.Diagnostics;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Integraciones.Aw.Application.Exceptions;
using Millet.Integraciones.Aw.Application.IntegrationEvents;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Domain.Exceptions;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Application.Commands.RegistrarCotizacionEdi;

/// <summary>
/// Handler de <see cref="RegistrarCotizacionEdiCommand"/>. Crea la
/// entidad raíz <see cref="EntidadExterna"/> en estado
/// <see cref="EstadoEntidad.Submitted"/> + emite
/// <see cref="AwCotizacionRecibida"/> al Outbox dentro de la misma TX.
///
/// <para>
/// La detección de <see cref="QuoteReferenceDuplicadaException"/> se
/// hace tanto por pre-check vía repositorio (mejor UX en happy path
/// duplicado) COMO por captura de <see cref="DbUpdateException"/> con
/// Postgres SqlState 23505 (defensa final contra race entre 2 requests
/// simultáneos — el UNIQUE constraint en BD es la fuente de verdad).
/// </para>
/// </summary>
public sealed class RegistrarCotizacionEdiHandler
    : IRequestHandler<RegistrarCotizacionEdiCommand, RegistrarCotizacionEdiResponse>
{
    private const string PostgresUniqueViolationSqlState = "23505";

    private readonly IntegracionesAwDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;
    private readonly ICurrentServicePrincipal _currentSp;
    private readonly IClock _clock;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly ILogger<RegistrarCotizacionEdiHandler> _logger;

    public RegistrarCotizacionEdiHandler(
        IntegracionesAwDbContext db,
        ICurrentEmpresaContext empresaContext,
        ICurrentServicePrincipal currentSp,
        IClock clock,
        IIntegrationEventPublisher publisher,
        ILogger<RegistrarCotizacionEdiHandler> logger)
    {
        _db = db;
        _empresaContext = empresaContext;
        _currentSp = currentSp;
        _clock = clock;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<RegistrarCotizacionEdiResponse> Handle(
        RegistrarCotizacionEdiCommand command,
        CancellationToken cancellationToken)
    {
        using var activity = IntegracionesAwActivitySource.Instance.StartActivity(
            "RegistrarCotizacionEdi", ActivityKind.Internal);
        activity?.SetTag("aw.quote_reference", command.QuoteReference);

        if (_empresaContext.Current is not Guid empresaId)
        {
            throw new MissingEmpresaContextException();
        }
        activity?.SetTag("aw.empresa_id", empresaId);

        // Pre-check de duplicado: mejor UX que esperar al UNIQUE
        // constraint a fallar (también atrapamos el race más abajo).
        var yaExiste = await _db.EntidadesExternas
            .AnyAsync(
                e => e.TipoEntidad == TipoEntidad.Cotizacion
                  && e.ReferenciaExterna == command.QuoteReference
                  && e.EmpresaId == empresaId,
                cancellationToken);

        if (yaExiste)
        {
            _logger.LogInformation(
                "RegistrarCotizacionEdi rechazado: quote_reference '{QuoteReference}' ya existe en empresa {EmpresaId}.",
                command.QuoteReference, empresaId);
            throw new QuoteReferenceDuplicadaException(
                TipoEntidad.Cotizacion, command.QuoteReference, empresaId);
        }

        var nowUtc = _clock.UtcNow;
        // Normalizar a uppercase (Agent puede mandar 'cir' o 'CIR'; el catálogo
        // y filename usan uppercase).
        var sucursalNormalizada = command.Sucursal.ToUpperInvariant();
        var entidad = new EntidadExterna(
            id: Guid.CreateVersion7(),
            tipoEntidad: TipoEntidad.Cotizacion,
            referenciaExterna: command.QuoteReference,
            empresaId: empresaId,
            payloadOriginal: command.PayloadOriginalJson,
            ediContent: command.EdiContent,
            submittedBySpnId: _currentSp.IsServicePrincipal ? _currentSp.Id : null,
            submittedAt: nowUtc,
            sucursal: sucursalNormalizada);

        _db.EntidadesExternas.Add(entidad);

        // Emite el integration event al buffer scoped; el
        // OutboxSaveChangesInterceptor lo drena en SavingChanges y crea
        // la fila en integraciones_aw.integration_events_outbox dentro
        // de la misma TX (consistencia atómica entrega).
        await _publisher.PublishAsync(
            new AwCotizacionRecibida(
                EmpresaId: empresaId,
                OcurridoEn: nowUtc,
                AggregateId: entidad.Id,
                QuoteReference: entidad.ReferenciaExterna,
                Sucursal: entidad.Sucursal,
                // Filename simplificado cot_<REF>.edi. El depto viaja dentro
                // del EDI (lo sobrescribe A+W), así que ya no se embebe la
                // sucursal en el nombre ni hace falta routing por lane en el
                // drop service (ver runbook aw-customizing v4). El quoteRef ya
                // da la unicidad del nombre.
                FilenameSuggestion: $"cot_{entidad.ReferenciaExterna}.edi"),
            cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Race: otra request ya creó la entidad con el mismo
            // (tipo, referencia, empresa). El UNIQUE constraint es la
            // fuente de verdad.
            _logger.LogWarning(ex,
                "RegistrarCotizacionEdi race UNIQUE: quote_reference '{QuoteReference}' creada por otra request.",
                command.QuoteReference);
            throw new QuoteReferenceDuplicadaException(
                TipoEntidad.Cotizacion, command.QuoteReference, empresaId);
        }

        activity?.SetTag("aw.entidad_externa_id", entidad.Id);
        return new RegistrarCotizacionEdiResponse(
            Id: entidad.Id,
            QuoteReference: entidad.ReferenciaExterna,
            Sucursal: entidad.Sucursal,
            Estado: entidad.Estado,
            SubmittedAt: entidad.SubmittedAt);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // Npgsql expone el SqlState en la PostgresException inner.
        // 23505 = unique_violation per Postgres errcodes.
        return ex.InnerException is Npgsql.PostgresException pgex
            && pgex.SqlState == PostgresUniqueViolationSqlState;
    }
}
