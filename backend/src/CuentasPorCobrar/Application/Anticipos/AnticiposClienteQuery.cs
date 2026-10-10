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
    : IRequest<IReadOnlyList<AnticipoSaldoClienteDto>>, Millet.SharedKernel.Application.ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_cobrar.cartera.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed class AnticiposClienteHandler
    : IRequestHandler<AnticiposClienteQuery, IReadOnlyList<AnticipoSaldoClienteDto>>
{
    private readonly IFacturacionAnticiposReadPort _anticipos;
    public AnticiposClienteHandler(IFacturacionAnticiposReadPort anticipos) { _anticipos = anticipos; }

    public async Task<IReadOnlyList<AnticipoSaldoClienteDto>> Handle(
        AnticiposClienteQuery query, CancellationToken cancellationToken)
    {
        var anticipos = await _anticipos.ListarPorClienteAsync(query.ClienteId, cancellationToken);
        return query.SucursalesPermitidas is null ? anticipos : anticipos.Where(x => x.SucursalId is Guid id && query.SucursalesPermitidas.Contains(id)).ToArray();
    }
}
