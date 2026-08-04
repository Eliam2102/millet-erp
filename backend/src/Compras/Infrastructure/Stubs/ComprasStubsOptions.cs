namespace Millet.Compras.Infrastructure.Stubs;

/// <summary>
/// Configuración bindeada de los stubs cross-module de Compras
/// (F3-PR2). Sección raíz: <c>Compras</c>.
///
/// <para>
/// El switch <see cref="UseStubs"/> activa el wiring de las
/// implementaciones <c>InMemory*</c> en <c>Program.cs</c>. Se valida en
/// arranque: si está en <c>true</c> y el environment es
/// <c>Production</c>, el bootstrap falla con mensaje explícito.
/// </para>
/// </summary>
public sealed class ComprasStubsOptions
{
    /// <summary>Sección raíz para binding desde <c>IConfiguration</c>.</summary>
    public const string SectionName = "Compras";

    /// <summary>
    /// Si <c>true</c>, los puertos cross-module se resuelven con
    /// implementaciones <c>InMemory*</c>. Default <c>false</c>.
    /// </summary>
    public bool UseStubs { get; set; }

    public StockStubOptions Stubs { get; set; } = new();
}

/// <summary>
/// Sub-sección <c>Compras:Stubs</c>.
/// </summary>
public sealed class StockStubOptions
{
    public StockOptions Stock { get; set; } = new();
}

/// <summary>
/// Sub-sección <c>Compras:Stubs:Stock</c>: gobierna el comportamiento
/// del <c>InMemoryConsultarStockPort</c> y, en cascada, del
/// <c>InMemoryReservarStockPort</c>.
///
/// <para>
/// <see cref="DefaultRatio"/> es la fracción de la cantidad solicitada
/// que se considera "disponible" cuando un artículo no tiene override.
/// Rango razonable [0, 1]; valores fuera de rango se truncan en el
/// stub.
/// </para>
/// <para>
/// <see cref="Ratios"/> permite override por artículo. Las claves son
/// representación string del <c>Guid</c> del artículo (binding nativo
/// de <c>IConfiguration</c> con keys de diccionario).
/// </para>
/// </summary>
public sealed class StockOptions
{
    public decimal DefaultRatio { get; set; } = 1.0m;

    public Dictionary<string, decimal> Ratios { get; set; } = new();
}
