namespace Millet.Integraciones.Fiscal.Domain.Ports;

/// <summary>
/// Envía el aviso de vencimiento del CSD (F6.9) a Fiscal y TI. Mientras no
/// exista el servicio de correo del ERP, la implementación solo deja el aviso
/// en el log; el aviso dentro del ERP sale del estado del CSD.
/// </summary>
public interface IAvisoCsdPort
{
    Task EnviarAsync(AvisoCsd aviso, IReadOnlyList<DestinatarioAvisoCsd> destinatarios, CancellationToken cancellationToken);
}

public sealed record AvisoCsd(
    Guid EmpresaId,
    string NumeroSerie,
    DateTimeOffset VenceEn,
    int DiasRestantes,
    int UmbralDias);

public sealed record DestinatarioAvisoCsd(string Nombre, string Email);

/// <summary>
/// Usuarios activos de la empresa con alguno de los roles indicados (F6.9,
/// V29: por omisión Contador General y administrador del sistema, a validar).
/// La implementación vive en <c>Millet.Api</c> y lee Identidad.
/// </summary>
public interface IDestinatariosAvisoCsdPort
{
    Task<IReadOnlyList<DestinatarioAvisoCsd>> ObtenerAsync(
        Guid empresaId, IReadOnlyList<string> codigosRol, CancellationToken cancellationToken);
}
