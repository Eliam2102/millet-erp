namespace Millet.Integraciones.Aw.Application.Clientes;

public enum AwClientesOrigenTipo { Simulado, Sql }

/// <summary>
/// Opciones de la sincronización de clientes A+W (ADM-06, doc integration/05 §10).
/// Sección <c>IntegracionesAw:Clientes</c>; conexión <c>ConnectionStrings:AwClientesDb</c>.
/// Todo apagado por defecto y sin relación con los flags/conexión del flujo de pedidos.
/// </summary>
public sealed class AwClientesOptions
{
    public const string SectionName = "IntegracionesAw:Clientes";
    public const string ConnectionStringName = "AwClientesDb";
    public const int TamanoLoteMaximo = 500;

    public AwClientesOrigenTipo Origen { get; set; } = AwClientesOrigenTipo.Simulado;
    public bool LecturaHabilitada { get; set; }
    public bool AplicacionHabilitada { get; set; }
    public bool ProgramacionHabilitada { get; set; }
    public int TamanoLote { get; set; } = 100;
    public int SqlQueryTimeoutSeconds { get; set; } = 5;
    public int SqlConnectTimeoutSeconds { get; set; } = 15;
    public int IntervaloProgramacionMinutos { get; set; } = 60;

    /// <summary>JSON opcional (formato de fixtures) para la fuente simulada.</summary>
    public string? ArchivoSimulado { get; set; }

    /// <summary>
    /// Código de moneda A+W (WAEHRUNG) → ISO. Vacío por defecto: sin mapeo validado
    /// no se normaliza (PESOSMX→MXN está PENDIENTE, doc 05 §4).
    /// </summary>
    public Dictionary<string, string> MapeoMoneda { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
