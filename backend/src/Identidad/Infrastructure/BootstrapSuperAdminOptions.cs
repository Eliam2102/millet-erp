namespace Millet.Identidad.Infrastructure;

/// <summary>
/// Configuración del bootstrap del primer SuperAdmin (ADR-0007). Se bindea
/// desde la sección <c>Auth</c>: el oid vive bajo
/// <c>Auth:InitialAdminEntraOid</c> y la empresa inicial bajo
/// <c>Auth:Bootstrap:EmpresaInicial:*</c>.
///
/// <para>
/// En QA/Prod el oid viene de Key Vault. En dev local de
/// <c>appsettings.Development.json</c> con un seed user de ADR-0015
/// (ej. <c>dev-superadmin</c>).
/// </para>
/// </summary>
public sealed class BootstrapSuperAdminOptions
{
    /// <summary>
    /// Sección padre <c>Auth</c>. El binding lo configura
    /// <c>AddMilletAuth</c>.
    /// </summary>
    public const string SectionName = "Auth";

    /// <summary>
    /// OID del SuperAdmin en Entra ID (string, GUID en prod o sintético
    /// en dev). Si null/empty, el bootstrap se omite — útil para entornos
    /// donde se quiera arrancar la app sin SuperAdmin (tests, perfilado).
    /// </summary>
    public string? InitialAdminEntraOid { get; set; }

    /// <summary>
    /// Configuración de la empresa inicial. Si null o sin <c>Rfc</c>, el
    /// bootstrap solo crea Usuario + Rol super-admin pero NO asigna a
    /// ninguna empresa (operador la crea después manualmente).
    /// </summary>
    public BootstrapEmpresaInicial? Bootstrap { get; set; }
}

/// <summary>
/// Sub-sección <c>Auth:Bootstrap</c>.
/// </summary>
public sealed class BootstrapEmpresaInicial
{
    public EmpresaInicialData? EmpresaInicial { get; set; }
}

/// <summary>
/// Datos de la empresa inicial creada por el bootstrap.
/// </summary>
public sealed class EmpresaInicialData
{
    public string Rfc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string RegimenFiscal { get; set; } = "601";
    public string? NombreComercial { get; set; }
}
