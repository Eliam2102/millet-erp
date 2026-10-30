namespace Millet.Api.Auth.Options;

/// <summary>
/// Reglas de acceso al ERP en el login. Sección <c>Identidad</c>.
/// </summary>
public sealed class IdentidadAccesoOptions
{
    public const string SectionName = "Identidad";

    /// <summary>
    /// «No hay acceso sin empleado» (86e3x6cpw, B3): un usuario que no es
    /// cuenta técnica y no está ligado a un empleado activo recibe 403
    /// <c>USUARIO_SIN_EMPLEADO</c>. Apagado por omisión para no dejar fuera a
    /// las cuentas actuales; se enciende por ambiente después de ligarlas a
    /// su ficha de empleado.
    /// </summary>
    public bool ExigirEmpleadoParaAcceso { get; set; }
}
