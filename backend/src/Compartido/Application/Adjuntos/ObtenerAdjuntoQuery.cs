using MediatR;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Application.Adjuntos;

/// <summary>Metadatos de un adjunto. Uno dado de baja solo lo ve quien tiene el permiso de baja.</summary>
public sealed record ObtenerAdjuntoQuery(string TipoEntidad, Guid EntidadId, Guid AdjuntoId) : IRequest<AdjuntoResponse>;

public sealed class ObtenerAdjuntoHandler : IRequestHandler<ObtenerAdjuntoQuery, AdjuntoResponse>
{
    private readonly AdjuntoAcceso _acceso;
    private readonly CompartidoDbContext _db;
    private readonly IClock _clock;

    public ObtenerAdjuntoHandler(AdjuntoAcceso acceso, CompartidoDbContext db, IClock clock)
    {
        _acceso = acceso;
        _db = db;
        _clock = clock;
    }

    public async Task<AdjuntoResponse> Handle(ObtenerAdjuntoQuery request, CancellationToken cancellationToken)
    {
        var (propietario, _) = await _acceso.AutorizarAsync(request.TipoEntidad, request.EntidadId, AdjuntoOperacion.Ver, cancellationToken);
        var adjunto = await AdjuntoSoporte.CargarAsync(_db, propietario.TipoEntidad, request.EntidadId, request.AdjuntoId, false, cancellationToken);

        if (adjunto.EstaDeBaja && !await _acceso.TienePermisoAsync(propietario.PermisoBaja, cancellationToken))
        {
            throw new EntityNotFoundException("ADJUNTO_NO_ENCONTRADO", $"No se encontró el adjunto '{request.AdjuntoId}'.");
        }

        var tipo = await AdjuntoSoporte.CargarTipoAsync(_db, adjunto.TipoDocumentoId, cancellationToken);
        return AdjuntoResponse.De(adjunto, tipo, AdjuntoSoporte.Hoy(_clock));
    }
}
