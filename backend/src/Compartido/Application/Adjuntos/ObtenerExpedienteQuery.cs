using MediatR;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;

namespace Millet.Compartido.Application.Adjuntos;

/// <summary>Expediente por tipo de documento (Faltante/Vigente/PorVencer/Vencido + adjunto actual).</summary>
public sealed record ObtenerExpedienteQuery(string TipoEntidad, Guid EntidadId) : IRequest<ExpedienteResponse>;

public sealed class ObtenerExpedienteHandler : IRequestHandler<ObtenerExpedienteQuery, ExpedienteResponse>
{
    private readonly AdjuntoAcceso _acceso;
    private readonly CompartidoDbContext _db;
    private readonly IClock _clock;

    public ObtenerExpedienteHandler(AdjuntoAcceso acceso, CompartidoDbContext db, IClock clock)
    {
        _acceso = acceso;
        _db = db;
        _clock = clock;
    }

    public async Task<ExpedienteResponse> Handle(ObtenerExpedienteQuery request, CancellationToken cancellationToken)
    {
        var (propietario, padre) = await _acceso.AutorizarAsync(
            request.TipoEntidad, request.EntidadId, AdjuntoOperacion.Ver, cancellationToken);
        return await ExpedienteAdjuntos.CalcularAsync(
            _db, propietario.TipoEntidad, request.EntidadId, padre.EsPersonaMoral,
            AdjuntoSoporte.Hoy(_clock), cancellationToken);
    }
}
