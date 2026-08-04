namespace Millet.CuentasPorCobrar.Domain.Cartera;

/// <summary>
/// Estados de una factura en la proyección de cartera (§4.1 del
/// 01-diseño). Se persisten como <c>short</c> con check constraint;
/// las transiciones ocurren SOLO por eventos de Facturación — nunca
/// por comandos del usuario.
/// </summary>
public enum EstadoFacturaCartera : short
{
    Abierta = 1,
    Parcial = 2,
    Pagada = 3,
    Cancelada = 4,
}
