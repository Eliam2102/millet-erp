namespace Millet.CuentasPorCobrar.Domain.Ports.Facturacion;

public interface IReppPendientesReadPort
{
    Task<ReppClienteEstado> ConsultarAsync(Guid clienteId, CancellationToken cancellationToken);
}

public sealed record ReppClienteEstado(bool TienePendientes, IReadOnlyList<Guid> RecibosEmitidos);
