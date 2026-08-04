using MediatR;
using Millet.Compras.Domain.Trazabilidad;

namespace Millet.Compras.Application.Trazabilidad.ObtenerArbolDocumentos;

public sealed record ObtenerArbolDocumentosQuery(
    TipoDocumentoTrazabilidad TipoDocumento,
    Guid Id) : IRequest<NodoArbolDocumento?>;
