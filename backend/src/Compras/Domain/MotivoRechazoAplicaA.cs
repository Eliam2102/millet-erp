namespace Millet.Compras.Domain;

/// <summary>
/// Bitmask que indica para qué tipo(s) de terminación aplica un
/// <see cref="MotivoRechazo"/>: rechazo de RQ (autorizador), eliminación
/// (solicitante pre-autorización), cancelación (post-autorización), y
/// rechazo de OC (autorizador de OC).
///
/// F3-PR2 agrega <see cref="OrdenCompra"/> = 8 para que los handlers de
/// rechazar OC puedan validar que el motivo aplica al flujo correcto.
/// Ver §3.bis.3 (RQ) y §3.3 del diseño OC.
///
/// ADR-0043 agrega <see cref="CierreManual"/> = 16 para el cierre manual de
/// RQ por el jefe de almacén (transición a CerradaSinSurtir/CerradaSurtidaParcial).
/// </summary>
[Flags]
public enum MotivoRechazoAplicaA : short
{
    Ninguno = 0,
    Rechazo = 1,
    Eliminacion = 2,
    Cancelacion = 4,
    OrdenCompra = 8,
    CierreManual = 16,
    Todos = Rechazo | Eliminacion | Cancelacion | OrdenCompra | CierreManual,
}
