using MediatR;

namespace Millet.Compras.Application.ObtenerRequisicionPorId;

/// <summary>
/// Query para obtener una requisición por ID. La empresa contextual la
/// determina el global query filter; usuarios sin acceso a la empresa
/// reciben 404 (no 403, evita enumeración cross-tenant — cuidado §8.1).
/// </summary>
public sealed record ObtenerRequisicionPorIdQuery(Guid Id) : IRequest<RequisicionResponse>;
