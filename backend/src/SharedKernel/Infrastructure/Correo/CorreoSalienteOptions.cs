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

    public LimitesCorreoOptions Limites { get; set; } = new();

    /// <summary>
    /// Vencimiento del client secret con Mail.Send (Key Vault). Vacío = sin aviso.
    /// El de erpmillet@ vence el 2027-01-27 (Jorge Toache, 10-nov-2026).
    /// </summary>
    public DateTimeOffset? SecretoVenceEl { get; set; }

    /// <summary>Días antes del vencimiento en que empieza el aviso (log diario y health check degradado).</summary>
    public int AvisoSecretoDiasAntes { get; set; } = 30;

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

/// <summary>
/// Control de volumen del buzón del ERP (86e3zr74h). Exchange Online permite
/// 30 mensajes por minuto, 500 destinatarios por mensaje y 10,000
/// destinatarios en 24 h por buzón; los valores por omisión quedan abajo de
/// esos límites para no tocar el límite de la organización (550 5.7.233).
/// </summary>
public sealed class LimitesCorreoOptions
{
    /// <summary>Mensajes por minuto; al llegar al tope el envío espera su turno.</summary>
    public int MensajesPorMinuto { get; set; } = 20;

    /// <summary>Destinatarios por mensaje; un mensaje con más se rechaza sin enviarse.</summary>
    public int MaxDestinatariosPorMensaje { get; set; } = 300;

    /// <summary>Destinatarios aceptados en las últimas 24 h; al llegar al tope el envío devuelve un error reintentable.</summary>
    public int MaxDestinatariosPorDia { get; set; } = 8000;

    /// <summary>Reintentos ante limitación del proveedor (429, 503, 5.7.233), con espera doble en cada uno.</summary>
    public int Reintentos { get; set; } = 3;

    public int EsperaInicialSegundos { get; set; } = 10;
}
