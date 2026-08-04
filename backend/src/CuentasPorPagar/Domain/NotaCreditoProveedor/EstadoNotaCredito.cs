namespace Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;

/// <summary>
/// Estados del ciclo de vida de una <see cref="NotaCreditoProveedor"/>
/// (§4.4 del 00-levantamiento + A19 del 01-diseno §3).
///
/// <list type="bullet">
///   <item><see cref="EnEspera"/> — capturada sin factura origen
///         (pre-factura). El worker
///         <c>NotaCreditoEnEsperaMatchWorker</c> intenta hacerle match
///         con facturas nuevas del proveedor por UUID de relación CFDI.</item>
///   <item><see cref="Abierta"/> — capturada con factura origen
///         resuelta; pendiente de aplicar al saldo.</item>
///   <item><see cref="Aplicada"/> — el monto se aplicó a la factura
///         origen (F6-PR2: AplicarNotaCreditoAFacturaCommand).</item>
///   <item><see cref="Cancelada"/> — descartada manualmente o porque
///         pasaron 30 días sin match (alerta al Auxiliar — política).</item>
/// </list>
/// </summary>
public enum EstadoNotaCredito
{
    EnEspera  = 1,
    Abierta   = 2,
    Aplicada  = 3,
    Cancelada = 4,
}
