namespace Millet.Facturacion.Domain.NotasCredito;

/// <summary>
/// F6.3: estado del reembolso en dinero de una NC, tal como lo reporta Tesorería. Sin valor = la NC no se reembolsa.
/// Se guarda como texto.
/// </summary>
public enum EstadoReembolsoNotaCredito
{
    /// <summary>Se pidió a Tesorería; está pendiente de autorizar.</summary>
    Solicitado = 1,
    Autorizado = 2,
    Rechazado = 3,
    Cancelado = 4,
    Pagado = 5,
}
