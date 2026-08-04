namespace Millet.Almacen.Domain.DevolucionesProveedor;

/// <summary>
/// Ciclo de vida del sub-flujo 8.B — Devolución a Proveedor
/// (01-diseno §4-§5). Distinto de <c>EstadoMovimiento</c> porque captura
/// la coordinación con Dirección + CxP:
///
/// <list type="bullet">
///   <item><see cref="Borrador"/> — el almacenista inició la solicitud.</item>
///   <item><see cref="EnAutorizacion"/> — esperando firma de Dirección.</item>
///   <item><see cref="Autorizada"/> — Dirección firmó; lista para registrar
///   la salida física.</item>
///   <item><see cref="Registrada"/> — salida física registrada, evento
///   <c>OcDevolucionRegistradaEvent</c> publicado; CxP debería generar
///   <c>NotaCargo</c>.</item>
///   <item><see cref="ConciliadaConNcFiscal"/> — CxP confirmó NC fiscal
///   tipo CFDI 03 vinculada (cierre del ciclo bidireccional).</item>
///   <item><see cref="Rechazada"/> — Dirección rechazó la solicitud.</item>
/// </list>
/// </summary>
public enum EstadoDevolucionProveedor : short
{
    Borrador = 0,
    EnAutorizacion = 1,
    Autorizada = 2,
    Registrada = 3,
    ConciliadaConNcFiscal = 4,
    Rechazada = 5,
}
