namespace Millet.Integraciones.Aw.Infrastructure.CostoPromedio;

/// <summary>
/// Costo promedio de un artículo en un almacén (M-447). Vidrio (con medida de hoja): <see cref="CostoM2"/>; material por
/// cantidad: <see cref="CostoPieza"/>. Siempre uno de los dos. Tipo y línea NULL = fila de sucursal; línea NULL = herraje.
/// </summary>
public sealed record CostoPromedioFila(
    int ArticuloAw, string Sucursal, string? AlmacenTipo, string? Linea, int? AnchoMm, int? AltoMm, decimal? CostoM2, decimal? CostoPieza)
{
    /// <summary>Moneda de A+W; el promedio del ERP siempre está en MXN.</summary>
    public const string Moneda = "PESOSMX";

    public decimal Costo => CostoM2 ?? CostoPieza ?? 0m;
}

/// <summary>Una fila escrita en <c>dbo.erp_costo_promedio</c> (el ERP solo inserta).</summary>
public sealed record CostoPromedioEscrito(CostoPromedioFila Fila, int Version, DateTimeOffset VigenteDesde)
{
    public string Moneda { get; init; } = CostoPromedioFila.Moneda;
}

public interface ICostoPromedioTransporte
{
    /// <summary>Inserta la fila. Idempotente por llave + versión: repetirla (reintento tras falla a medias) no duplica.</summary>
    Task EscribirAsync(CostoPromedioEscrito escrito, CancellationToken ct);
}

/// <summary>Sección <c>Integraciones:CostoPromedio</c>. Apagado por omisión; <c>Transporte</c>: Simulado | Sql.</summary>
public sealed class CostoPromedioOptions
{
    public const string SectionName = "Integraciones:CostoPromedio";

    public bool Habilitado { get; init; }
    public string Transporte { get; init; } = "Simulado";
    public int IntervaloSegundos { get; init; } = 60;
}
