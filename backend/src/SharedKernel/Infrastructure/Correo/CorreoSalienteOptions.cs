namespace Millet.SharedKernel.Infrastructure.Correo;

/// <summary>
/// Sección <c>Correo</c> (86e3txv0n). Sin secretos: la credencial de Graph es
/// la de la API (<c>Auth:EntraId</c>, ClientSecret desde Key Vault).
/// </summary>
public sealed class CorreoSalienteOptions
{
    public const string SectionName = "Correo";

    /// <summary><c>NoOp</c> (por omisión), <c>SandboxLocal</c> (Mailpit, solo Development) o <c>Graph</c>.</summary>
    public string Proveedor { get; set; } = "NoOp";

    /// <summary>Buzón que envía con Graph (erpmillet@). Si falta se usa <c>Auth:EntraId:SenderEmail</c>.</summary>
    public string Remitente { get; set; } = string.Empty;

    public string NombreRemitente { get; set; } = "ERP Millet";

    public SmtpSandboxOptions Smtp { get; set; } = new();

    public ModoPruebasCorreoOptions ModoPruebas { get; set; } = new();

    public bool EsGraph => Proveedor.Equals("Graph", StringComparison.OrdinalIgnoreCase);
    public bool EsSandboxLocal => Proveedor.Equals("SandboxLocal", StringComparison.OrdinalIgnoreCase);
    public bool EsNoOp => Proveedor.Equals("NoOp", StringComparison.OrdinalIgnoreCase);
}

public sealed class SmtpSandboxOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Puerto { get; set; } = 1025;
    public string Remitente { get; set; } = "erp@millet.local.test";

    public bool EsLoopback => Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || Host == "127.0.0.1" || Host == "::1";
}

/// <summary>
/// Servidor de pruebas: su base tiene clientes y proveedores reales, así que
/// todo destinatario fuera de <see cref="DominiosPermitidos"/> se cambia por
/// <see cref="BuzonesPrueba"/>.
/// </summary>
public sealed class ModoPruebasCorreoOptions
{
    public bool Activo { get; set; }
    public List<string> DominiosPermitidos { get; set; } = [];
    public List<string> BuzonesPrueba { get; set; } = [];
}
