namespace Millet.Facturacion.Domain.Ports;

/// <summary>Lee el plazo pactado del cliente en CxC para la moneda de la factura.</summary>
public interface IPlazoCreditoClienteReadPort
{
    /// <summary>Devuelve el plazo de una línea activa o bloqueada; sin línea, <c>null</c>.</summary>
    Task<int?> ObtenerPlazoDiasAsync(Guid clienteId, string moneda, CancellationToken ct);
}
