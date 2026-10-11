namespace Millet.Facturacion.Domain.Ports;

/// <summary>Candado del período contable; Contabilidad resuelve su estado por el puerto público.</summary>
public interface IPeriodoContablePort
{
    /// <summary>¿Está abierto el período (año, mes) para operar comprobantes?</summary>
    Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken cancellationToken);
}
