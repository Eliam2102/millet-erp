namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Consulta si el periodo existe y está abierto en Contabilidad (D9).
/// Los movimientos verifican además el cierre propio de inventario:
/// reabrir Contabilidad no reabre Almacén (D18).
/// </summary>
public interface IPeriodoContableReadPort
{
    Task<bool> EstaAbiertoAsync(
        int año,
        int mes,
        CancellationToken cancellationToken);
}
