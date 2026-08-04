using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Administracion.Application.Parametros;

/// <summary>
/// Query para listar parámetros globales. Si <see cref="Modulo"/> es
/// no-null, filtra a los del módulo; si es null, retorna todos los
/// parámetros del sistema y de todos los módulos.
///
/// <para>
/// Convención: <c>Modulo = ""</c> (string vacío) retorna SOLO los
/// globales del sistema (filas con <c>Modulo IS NULL</c>).
/// </para>
/// </summary>
public sealed record ListarParametrosQuery(string? Modulo = null) : IRequest<ListarParametrosResponse>;

public sealed class ListarParametrosHandler
    : IRequestHandler<ListarParametrosQuery, ListarParametrosResponse>
{
    private readonly CompartidoDbContext _db;

    public ListarParametrosHandler(CompartidoDbContext db)
    {
        _db = db;
    }

    public async Task<ListarParametrosResponse> Handle(
        ListarParametrosQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.ParametrosGlobales.AsNoTracking();

        if (request.Modulo is "")
        {
            query = query.Where(p => p.Modulo == null);
        }
        else if (request.Modulo is { Length: > 0 })
        {
            query = query.Where(p => p.Modulo == request.Modulo);
        }

        var items = await query
            .OrderBy(p => p.Modulo).ThenBy(p => p.Clave)
            .Select(p => new ParametroResponse(
                p.Id, p.Clave, p.Valor, p.Tipo, p.Modulo, p.Descripcion, p.Version))
            .ToListAsync(cancellationToken);

        return new ListarParametrosResponse(items);
    }
}
