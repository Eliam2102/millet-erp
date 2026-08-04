using MediatR;
using Millet.Compras.Domain.Trazabilidad;

namespace Millet.Compras.Application.Trazabilidad.ObtenerArbolDocumentos;

public sealed class ObtenerArbolDocumentosHandler
    : IRequestHandler<ObtenerArbolDocumentosQuery, NodoArbolDocumento?>
{
    private readonly IObtenerArbolDocumentosService _service;

    public ObtenerArbolDocumentosHandler(IObtenerArbolDocumentosService service)
    {
        _service = service;
    }

    public Task<NodoArbolDocumento?> Handle(
        ObtenerArbolDocumentosQuery request, CancellationToken cancellationToken)
    {
        return _service.ObtenerAsync(request.TipoDocumento, request.Id, cancellationToken);
    }
}
