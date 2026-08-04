namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Servicio de notificación que entrega el CFDI al correo del cliente (§12.5
/// levantamiento). F2-PR2 lo cablea con un stub que loggea
/// (<c>PLATFORM-TODO(&lt;EnvioCfdiCliente&gt;)</c>); se reemplaza por
/// <c>Millet.Integraciones.Mailbox</c> / SMTP/Graph cuando exista.
/// </summary>
public interface INotificacionService
{
    /// <summary>Entrega el CFDI por correo. Devuelve <c>true</c> si fue aceptado para envío.</summary>
    Task<bool> EnviarCfdiPorCorreoAsync(EnvioCfdiCorreo envio, CancellationToken cancellationToken);
}

public sealed record EnvioCfdiCorreo(
    Guid EmpresaId,
    Guid ComprobanteId,
    string Destinatario,
    string Folio,
    string? Uuid,
    byte[]? PdfAdjunto,
    string? XmlAdjunto);
