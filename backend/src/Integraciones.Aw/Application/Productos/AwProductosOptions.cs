namespace Millet.Integraciones.Aw.Application.Productos;

/// <summary><c>Postgres</c> = origen de demo (tablas <c>aw_origen.*</c>, <c>tools/aw-origen-demo</c>), no A+W.</summary>
public enum AwProductosOrigenTipo { Simulado, Sql, Postgres }

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

    /// <summary>
    /// Datos fiscales por tipo <c>BA_PRODUKTART</c> de A+W, tomados de los artículos genéricos de SAP (la carga inicial
    /// del dato fiscal; el ERP queda como dueño). Se aplican al crear el producto y, en uno existente, solo a lo vacío.
    /// ponytail: configuración estática; pasar a tabla editable si Fiscal quiere mantenerla sin despliegue.
    /// </summary>
    public Dictionary<string, ReglaFiscalProductoAw> ReglasFiscales { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ReglaFiscalProductoAw
{
    public string? ClaveProdServSat { get; set; }
    public string? ClaveUnidadSat { get; set; }
    public string? ObjetoImp { get; set; }
    public decimal? TasaIvaTraslado { get; set; }
    public string? FraccionArancelaria { get; set; }
    public string? UnidadAduana { get; set; }
    /// <summary>Artículo SAP del que sale la regla (trazabilidad), p. ej. VID80001.</summary>
    public string? ArticuloSap { get; set; }
}
