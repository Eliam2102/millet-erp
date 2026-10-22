namespace Millet.CuentasPorCobrar.Domain.Ports.Correo;

/// <summary>
/// Salida de correo del estado de cuenta al cliente (C7.8). En
/// Development se resuelve con SMTP de captura en loopback (Mailpit); en
/// los demás ambientes con un NoOp hasta que exista el motor de correo
/// transversal (PLATFORM-TODO &lt;EnvioEstadoCuentaCliente&gt;, ADR-0031).
/// </summary>
public interface IEstadoCuentaCorreoPort
{
    /// <summary>
    /// Envía el correo. Devuelve <c>true</c> si el transporte lo aceptó y
    /// <c>false</c> si no hay transporte configurado (el envío queda solo
    /// registrado). Cualquier excepción se interpreta como fallo de envío.
    /// </summary>
    Task<bool> EnviarAsync(CorreoEstadoCuenta correo, CancellationToken cancellationToken);
}

public sealed record CorreoEstadoCuenta(
    string Destinatario,
    string RazonSocialCliente,
    string? Mensaje,
    string NombreArchivo,
    byte[] Pdf);
