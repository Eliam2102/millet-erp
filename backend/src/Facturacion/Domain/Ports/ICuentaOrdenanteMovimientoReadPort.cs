namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// 86e3tfdve (opción A): cuenta ordenante que Tesorería capturó al registrar el
/// ingreso. No viaja en <c>tesoreria.pago-cliente.confirmado.v1</c> (cuidados-infra
/// §4 de Tesorería: el payload no lleva datos bancarios del cliente); Facturación
/// la lee con el id del movimiento al crear el pendiente de REP. El adaptador vive
/// en <c>Millet.Api</c> y consulta el puerto público de Tesorería.
/// </summary>
public interface ICuentaOrdenanteMovimientoReadPort
{
    /// <summary>Cuenta completa (sin enmascarar) o <c>null</c> si el movimiento no la tiene.</summary>
    Task<string?> ObtenerAsync(Guid movimientoBancarioId, CancellationToken cancellationToken);
}
