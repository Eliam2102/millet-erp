using MediatR;
using Millet.Compras.Application.Oc.ListarOrdenesCompra;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Application.Oc.ListarPendientesAutorizacion;

/// <summary>
/// Bandeja de OCs pendientes de autorización (F6-PR3). Devuelve OCs en
/// <see cref="EstadoOrdenCompra.EnAutorizacionJefeCompras"/> o
/// <see cref="EstadoOrdenCompra.EnAutorizacionDireccion"/>. Si se pasa
/// <see cref="Nivel"/>, restringe al nivel específico. Útil para inboxes
/// de N1 (Jefe Compras) y N2 (Dirección).
/// </summary>
public sealed record ListarPendientesAutorizacionOcQuery(
    NivelAutorizacion? Nivel = null,
    int Page = 1,
    int PageSize = 50) : IRequest<ListarOrdenesCompraResponse>;
