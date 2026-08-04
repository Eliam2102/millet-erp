namespace Millet.CuentasPorCobrar.Domain.Ports.Facturacion;

/// <summary>
/// Saldo de un anticipo del cliente, expuesto por Facturación
/// (promoción de <c>AnticipoSaldoDetalle</c> a contrato consumible,
/// CXC-PR3 / gap G-anticipos-port del levantamiento).
/// </summary>
public sealed record AnticipoSaldoClienteDto(
    Guid AnticipoId,
    Guid ClienteId,
    string Estado,
    decimal MontoCobrado,
    decimal MontoAmortizado,
    decimal Saldo,
    decimal SaldoDisponible,
    string Moneda,
    string? PedidoOrigenRef);

/// <summary>
/// Puerto de lectura de saldos de anticipo por cliente (§6.1 del
/// 01-diseño). El adapter vive en Facturación
/// (<c>Infrastructure/PublicAdapters</c>) — CxC nunca lee tablas del
/// esquema <c>facturacion</c>. Los anticipos NO son cartera: alimentan
/// el estado de cuenta y la vista de "dinero del cliente en la casa".
/// </summary>
public interface IFacturacionAnticiposReadPort
{
    Task<IReadOnlyList<AnticipoSaldoClienteDto>> ListarPorClienteAsync(
        Guid clienteId, CancellationToken cancellationToken);
}
