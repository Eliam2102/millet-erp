using MediatR;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
namespace Millet.CuentasPorPagar.Application.Catalogos;
public sealed record ListarDependenciasRevisorasQuery : IRequest<IReadOnlyList<DependenciaRevisoraDto>>;
public sealed class ListarDependenciasRevisorasHandler(IDependenciaRevisoraReadPort port)
    : IRequestHandler<ListarDependenciasRevisorasQuery, IReadOnlyList<DependenciaRevisoraDto>>
{
    public Task<IReadOnlyList<DependenciaRevisoraDto>> Handle(ListarDependenciasRevisorasQuery query, CancellationToken cancellationToken) => port.ListarAsync(cancellationToken);
}
