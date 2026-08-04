using MediatR;

namespace Millet.Compras.Application.Oc.ObtenerOrdenCompraPorId;

/// <summary>
/// Query para obtener una OC por ID. La empresa contextual la determina
/// el global query filter; usuarios sin acceso a la empresa reciben 404
/// (no 403, evita enumeración cross-tenant — mismo patrón que RQ).
/// </summary>
public sealed record ObtenerOrdenCompraPorIdQuery(Guid Id) : IRequest<OrdenCompraResponse>;
