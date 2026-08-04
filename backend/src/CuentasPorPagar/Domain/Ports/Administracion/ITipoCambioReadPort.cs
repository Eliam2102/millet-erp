namespace Millet.CuentasPorPagar.Domain.Ports.Administracion;

/// <summary>
/// Puerto de lectura del catálogo de tipos de cambio (ADR-0014).
/// CxP lo consume al capturar facturas en moneda extranjera, anticipos
/// y movimientos de TC en USD.
///
/// <para>
/// F0-PR1 introduce el contrato + stub <c>NoOpTipoCambioReadPort</c>
/// (devuelve 1.0 para MXN, valor configurable para otras monedas).
/// Adapter real cuando Catálogos/Administración expongan
/// <c>tipos_cambio</c> con histórico.
/// </para>
/// </summary>
public interface ITipoCambioReadPort
{
    Task<decimal?> ObtenerAsync(
        string monedaOrigen,
        string monedaDestino,
        DateOnly fecha,
        CancellationToken cancellationToken);
}
