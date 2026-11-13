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
    /// <summary>Al terminar un barrido, unifica los grupos de RFC repetido sin historial (acuerdo Millet 27-oct). Apagado por defecto.</summary>
    public bool UnificacionAutomaticaHabilitada { get; set; }
    public int TamanoLote { get; set; } = 100;
    public int SqlQueryTimeoutSeconds { get; set; } = 5;
    public int SqlConnectTimeoutSeconds { get; set; } = 15;
    public int IntervaloProgramacionMinutos { get; set; } = 60;

    /// <summary>
    /// M-299: a quién se le abre la incidencia de un cliente cuya marca de bloqueo de A+W no tiene equivalencia.
    /// Sin los dos datos no se abre (queda advertencia en el log y el cliente sigue Pendiente en la bitácora).
    /// </summary>
    public AwClientesIncidenciasOptions Incidencias { get; set; } = new();

    /// <summary>JSON opcional (formato de fixtures) para la fuente simulada.</summary>
    public string? ArchivoSimulado { get; set; }

    /// <summary>
    /// Código de moneda A+W (WAEHRUNG) → ISO (appsettings: PESOSMX, USD, Euro). Lo que no está aquí,
    /// incluidos <c>&lt;indf&gt;</c> y vacío, se toma como MXN (doc 05 §4).
    /// </summary>
    public Dictionary<string, string> MapeoMoneda { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AwClientesIncidenciasOptions
{
    public Guid? EmpresaId { get; set; }
    public Guid? ResponsableUsuarioId { get; set; }
}
