namespace Millet.Integraciones.Aw.Application.Productos;

public enum AwProductosOrigenTipo { Simulado, Sql }

/// <summary>
/// Opciones de la sincronización de productos A+W (ADM-07, doc integration/06).
/// Sección <c>IntegracionesAw:Productos</c>. Apagado por defecto.
/// </summary>
public sealed class AwProductosOptions
{
    public const string SectionName = "IntegracionesAw:Productos";
    public const int TamanoLoteMaximo = 500;
    public const string ConnectionStringName = "AwProductosDb";
    public const string VersionContrato = "1";
    public const string VersionMapeo = "1-componentes";

    /// <summary>Interruptor maestro; con <see cref="Origen"/> = Sql además exige <c>ConnectionStrings:AwProductosDb</c>.</summary>
    public bool OrigenHabilitado { get; set; }
    public AwProductosOrigenTipo Origen { get; set; } = AwProductosOrigenTipo.Simulado;
    public int SqlQueryTimeoutSeconds { get; set; } = 15;
    public int SqlConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>Tipos <c>BA_PRODUKTART</c> a excluir (decisión P1 pendiente de Millet); vacío = no excluye nada.</summary>
    public string[] TiposExcluidos { get; set; } = [];
    public int TamanoLote { get; set; } = 100;

    /// <summary>JSON (formato de fixtures) de la fuente simulada; sin él no hay origen.</summary>
    public string? ArchivoSimulado { get; set; }
}
