using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Application.Adjuntos;

/// <summary>Lista los adjuntos de una entidad. <c>IncluirBajas</c> exige el permiso de baja.</summary>
public sealed record ListarAdjuntosQuery(string TipoEntidad, Guid EntidadId, bool IncluirBajas = false)
    : IRequest<IReadOnlyList<AdjuntoResponse>>;

public sealed class ListarAdjuntosHandler : IRequestHandler<ListarAdjuntosQuery, IReadOnlyList<AdjuntoResponse>>
{
    private readonly AdjuntoAcceso _acceso;
    private readonly CompartidoDbContext _db;
    private readonly IClock _clock;

    public ListarAdjuntosHandler(AdjuntoAcceso acceso, CompartidoDbContext db, IClock clock)
    {
        _acceso = acceso;
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<AdjuntoResponse>> Handle(ListarAdjuntosQuery request, CancellationToken cancellationToken)
    {
        var (propietario, _) = await _acceso.AutorizarAsync(request.TipoEntidad, request.EntidadId, AdjuntoOperacion.Ver, cancellationToken);
        if (request.IncluirBajas)
        {
            await _acceso.AutorizarPermisoAsync(propietario.TipoEntidad, AdjuntoOperacion.Baja, cancellationToken);
        }

        var adjuntos = _db.Adjuntos.AsNoTracking()
            .Where(a => a.TipoEntidad == propietario.TipoEntidad && a.EntidadId == request.EntidadId);
        if (!request.IncluirBajas) adjuntos = adjuntos.Where(a => a.BajaEn == null);

        var filas = await adjuntos.OrderByDescending(a => a.SubidoEn).ToListAsync(cancellationToken);
        var tipos = await _db.AdjuntoTiposDocumento.AsNoTracking()
            .Where(t => t.TipoEntidad == propietario.TipoEntidad)
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var hoy = AdjuntoSoporte.Hoy(_clock);
        return filas.Select(a => AdjuntoResponse.De(a, tipos[a.TipoDocumentoId], hoy)).ToList();
    }
}
