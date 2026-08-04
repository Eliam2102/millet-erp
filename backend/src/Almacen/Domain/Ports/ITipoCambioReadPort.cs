namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia Catálogos/Administración para obtener T/C
/// del día (ADR-0014). Lo usan los handlers de recepción cuando la OC
/// está en moneda distinta a MXN y necesita convertir el costo de
/// inventario al momento del movimiento.
/// </summary>
public interface ITipoCambioReadPort
{
    Task<decimal?> ObtenerTipoCambioMxnAsync(
        string monedaOrigen,
        DateOnly fecha,
        CancellationToken cancellationToken);
}
