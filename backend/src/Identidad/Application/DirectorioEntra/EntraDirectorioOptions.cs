namespace Millet.Identidad.Application.DirectorioEntra;

/// <summary>
/// Configuración del directorio de Entra ID para el alta unificada
/// (plan 15, F2). Sección <c>Entra</c>.
/// </summary>
public sealed class EntraDirectorioOptions
{
    public const string SectionName = "Entra";

    /// <summary>
    /// Dominios en los que el ERP puede crear o vincular cuentas
    /// corporativas. Vacío = ninguno (valor seguro por defecto en QA/Prod
    /// hasta que TI confirme la lista, plan 15 §8).
    /// </summary>
    public List<string> DominiosPermitidos { get; set; } = [];

    /// <summary>
    /// Cuentas que el directorio simulado trae precargadas, para probar el
    /// camino "Ya tiene cuenta Microsoft" sin Graph.
    /// </summary>
    public EntraSimulacionOptions Simulacion { get; set; } = new();

    /// <summary>
    /// Liga de inicio de sesión que lleva el correo de acceso de una cuenta
    /// nueva (camino B, F4). Por defecto el portal de Microsoft; en cada
    /// ambiente conviene apuntarla al ERP.
    /// </summary>
    public string UrlInicioSesion { get; set; } = "https://myapps.microsoft.com";

    /// <summary>Worker de provisión de cuentas nuevas (camino B, F4).</summary>
    public ProvisionCuentaEntraOptions Provision { get; set; } = new();

    public bool PermiteCorreo(string correo)
    {
        var dominio = ObtenerDominio(correo);
        return dominio is not null
            && DominiosPermitidos.Any(d => string.Equals(
                d.Trim(), dominio, StringComparison.OrdinalIgnoreCase));
    }

    public static string? ObtenerDominio(string correo)
    {
        var arroba = correo.LastIndexOf('@');
        return arroba > 0 && arroba < correo.Length - 1 ? correo[(arroba + 1)..] : null;
    }
}

public sealed class EntraSimulacionOptions
{
    public List<CuentaSimuladaOptions> CuentasExistentes { get; set; } = [];

    /// <summary>Buzón SMTP de captura exclusivamente local (por ejemplo Mailpit).</summary>
    public CorreoSandboxOptions CorreoSandbox { get; set; } = new();
}

public sealed class CorreoSandboxOptions
{
    public string Host { get; set; } = string.Empty;
    public int Puerto { get; set; } = 1025;
}

public sealed class CuentaSimuladaOptions
{
    public string Correo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Opcional: si falta se deriva de forma estable del correo.</summary>
    public string? ObjectId { get; set; }

    public bool Habilitada { get; set; } = true;
}

public sealed class ProvisionCuentaEntraOptions
{
    /// <summary>Apaga el worker (el alta con cuenta nueva queda en espera).</summary>
    // En Development puede habilitarse con directorio simulado y SMTP local;
    // el valor seguro por defecto en los demás ambientes sigue siendo true.
    public bool Disabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 15;

    /// <summary>Usuarios en provisión que atiende cada ciclo.</summary>
    public int BatchSize { get; set; } = 10;
}
