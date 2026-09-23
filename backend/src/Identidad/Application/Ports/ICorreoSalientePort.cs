namespace Millet.Identidad.Application.Ports;

/// <summary>
/// Puerto out-port para el correo que entrega el acceso a un colaborador
/// con cuenta Microsoft nueva (plan 15, F4, D6). El adaptador real envía
/// con <c>Mail.Send</c> de Graph desde un buzón de servicio (plan 15 §8);
/// hoy solo existe <c>CorreoSalienteSimulado</c> (ADR-0015).
/// </summary>
public interface ICorreoSalientePort
{
    /// <summary>
    /// Envía el correo de acceso. Lanza si el envío no se aceptó; el
    /// llamador decide si reintenta.
    /// </summary>
    Task EnviarAccesoColaboradorAsync(CorreoAccesoColaborador correo, CancellationToken ct);
}

/// <summary>
/// Contenido del correo de acceso. La <see cref="ContrasenaTemporal"/>
/// solo viaja en memoria: nunca se persiste ni se registra en logs o
/// auditoría (plan 15, D6).
/// </summary>
public sealed record CorreoAccesoColaborador(
    string Destinatario,
    string NombreColaborador,
    string Upn,
    string ContrasenaTemporal,
    string UrlInicioSesion)
{
    // El record no debe filtrar la contraseña si alguien lo loguea.
    public override string ToString() =>
        $"CorreoAccesoColaborador {{ Destinatario = {Destinatario}, Upn = {Upn} }}";
}
