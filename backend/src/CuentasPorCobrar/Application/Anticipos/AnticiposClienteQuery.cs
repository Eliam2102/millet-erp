using MediatR;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;

namespace Millet.CuentasPorCobrar.Application.Anticipos;

/// <summary>
/// CXC-PR3: saldos de anticipo del cliente vía
/// <see cref="IFacturacionAnticiposReadPort"/> (§1.4 del levantamiento —
/// "dinero del cliente en la casa"). Proxy puro: la fuente de verdad es
/// Facturación; CxC no proyecta anticipos.
/// </summary>
public sealed record AnticiposClienteQuery(Guid ClienteId)
    : IRequest<IReadOnlyList<AnticipoSaldoClienteDto>>;

public sealed class AnticiposClienteHandler
    : IRequestHandler<AnticiposClienteQuery, IReadOnlyList<AnticipoSaldoClienteDto>>
{
    private readonly IFacturacionAnticiposReadPort _anticipos;
    public AnticiposClienteHandler(IFacturacionAnticiposReadPort anticipos) { _anticipos = anticipos; }

    public Task<IReadOnlyList<AnticipoSaldoClienteDto>> Handle(
        AnticiposClienteQuery query, CancellationToken cancellationToken) =>
        _anticipos.ListarPorClienteAsync(query.ClienteId, cancellationToken);
}
