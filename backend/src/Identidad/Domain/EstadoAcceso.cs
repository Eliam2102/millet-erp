namespace Millet.Identidad.Domain;

/// <summary>
/// Ciclo de vida del acceso de un <see cref="Usuario"/> respecto de su
/// cuenta en Microsoft Entra ID (alta unificada de colaboradores,
/// F1-ADM-01 plan 15). Es ortogonal a <see cref="Usuario.Activo"/>, que
/// sigue marcando la baja lógica.
/// </summary>
public enum EstadoAcceso : short
{
    /// <summary>Ya inició sesión al menos una vez (o es un usuario previo al alta unificada con OID real).</summary>
    Activo = 0,

    /// <summary>La cuenta existe en Entra; la persona aún no inicia sesión.</summary>
    PendientePrimerAcceso = 1,

    /// <summary>Se pidió crear la cuenta en Entra vía Graph y el worker aún no termina.</summary>
    ProvisionandoCuenta = 2,

    /// <summary>Graph rechazó la creación de la cuenta; el admin puede reintentar.</summary>
    ErrorProvision = 3,
}
