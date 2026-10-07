using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Adjuntos;

namespace Millet.Compartido.Application.Adjuntos;

/// <summary>Catálogo de tipos de documento activos de un tipo de entidad (requiere el permiso de ver).</summary>
public sealed record ListarTiposDocumentoQuery(string TipoEntidad) : IRequest<IReadOnlyList<AdjuntoTipoDocumentoResponse>>;

public sealed class ListarTiposDocumentoHandler
    : IRequestHandler<ListarTiposDocumentoQuery, IReadOnlyList<AdjuntoTipoDocumentoResponse>>
{
    private readonly AdjuntoAcceso _acceso;
    private readonly CompartidoDbContext _db;

    public ListarTiposDocumentoHandler(AdjuntoAcceso acceso, CompartidoDbContext db)
    {
        _acceso = acceso;
        _db = db;
    }

    public async Task<IReadOnlyList<AdjuntoTipoDocumentoResponse>> Handle(
        ListarTiposDocumentoQuery request, CancellationToken cancellationToken)
    {
        var propietario = await _acceso.AutorizarPermisoAsync(request.TipoEntidad, AdjuntoOperacion.Ver, cancellationToken);
        return await _db.AdjuntoTiposDocumento.AsNoTracking()
            .Where(t => t.TipoEntidad == propietario.TipoEntidad && t.Activo)
            .OrderBy(t => t.Orden)
            .Select(t => new AdjuntoTipoDocumentoResponse(
                t.Id, t.Codigo, t.Nombre, t.Orden, t.Obligatorio, t.VigenciaMeses, t.SoloPersonaMoral))
            .ToListAsync(cancellationToken);
    }
}
