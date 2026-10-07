using MediatR;
using Millet.Administracion.Application.Auditoria;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;

namespace Millet.Compartido.Application.Adjuntos;

/// <summary>
/// Bitácora de un adjunto (quién, cuándo, qué, por qué) reutilizando <see cref="ConsultarBitacoraQuery"/>.
/// Requiere el permiso de baja. Sin fechas: desde el alta (o los últimos 90 días) hasta hoy.
/// </summary>
public sealed record ConsultarBitacoraAdjuntoQuery(
    string TipoEntidad,
    Guid EntidadId,
    Guid AdjuntoId,
    DateOnly? Desde = null,
    DateOnly? Hasta = null,
    int Offset = 0,
    int Limit = 50) : IRequest<ConsultarBitacoraResponse>;

public sealed class ConsultarBitacoraAdjuntoHandler
    : IRequestHandler<ConsultarBitacoraAdjuntoQuery, ConsultarBitacoraResponse>
{
    private const string ZonaHoraria = "America/Mexico_City";

    private readonly AdjuntoAcceso _acceso;
    private readonly CompartidoDbContext _db;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public ConsultarBitacoraAdjuntoHandler(AdjuntoAcceso acceso, CompartidoDbContext db, IMediator mediator, IClock clock)
    {
        _acceso = acceso;
        _db = db;
        _mediator = mediator;
        _clock = clock;
    }

    public async Task<ConsultarBitacoraResponse> Handle(ConsultarBitacoraAdjuntoQuery request, CancellationToken cancellationToken)
    {
        var (propietario, _) = await _acceso.AutorizarAsync(
            request.TipoEntidad, request.EntidadId, AdjuntoOperacion.Baja, cancellationToken);
        var adjunto = await AdjuntoSoporte.CargarAsync(
            _db, propietario.TipoEntidad, request.EntidadId, request.AdjuntoId, false, cancellationToken);

        // ponytail: la bitácora limita el rango a 90 días; por defecto, desde el alta si es reciente.
        var hasta = request.Hasta ?? AdjuntoSoporte.Hoy(_clock);
        var desde = request.Desde ?? DateOnly.FromDateTime(adjunto.SubidoEn.UtcDateTime);
        if (desde > hasta) desde = hasta;
        if (hasta.DayNumber - desde.DayNumber > 90) desde = hasta.AddDays(-90);

        return await _mediator.Send(new ConsultarBitacoraQuery(
            desde, hasta,
            Recurso: "Adjunto",
            EntidadId: adjunto.Id,
            Offset: request.Offset,
            Limit: request.Limit,
            ZonaHoraria: ZonaHoraria), cancellationToken);
    }
}
