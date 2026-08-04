namespace Millet.Facturacion.Domain.Cce;

/// <summary>
/// Mercancía de un <see cref="ComplementoCce"/> (nodo <c>Mercancia</c> del
/// Complemento Comercio Exterior; §4.5 levantamiento). Datos aduaneros por línea:
/// fracción arancelaria, unidad aduanera (típicamente kg, con conversión), valor
/// en dólares y la bandera de IVA 0%. Entidad hija de <see cref="ComplementoCce"/>.
/// </summary>
public sealed class ComplementoCceLinea
{
    public Guid Id { get; private set; }
    public Guid ComplementoCceId { get; private set; }

    /// <summary>Fracción arancelaria (<c>c_FraccionArancelaria</c>, 8-10 dígitos).</summary>
    public string FraccionArancelaria { get; private set; } = string.Empty;

    /// <summary>Clave de unidad aduanera (<c>c_UnidadAduana</c>, p.ej. 01 = kg).</summary>
    public string UnidadAduana { get; private set; } = string.Empty;

    /// <summary>Cantidad en la unidad aduanera (puede diferir de la cantidad de la factura).</summary>
    public decimal CantidadAduana { get; private set; }

    /// <summary>Valor unitario aduanero en dólares (USD).</summary>
    public decimal ValorUnitarioAduana { get; private set; }

    /// <summary>Valor total en dólares (USD) de la mercancía.</summary>
    public decimal ValorDolares { get; private set; }

    /// <summary>True = aplica IVA 0% (exportación); false = no objeto de IVA.</summary>
    public bool AplicaIva0 { get; private set; }

    private ComplementoCceLinea() { }

    internal ComplementoCceLinea(
        Guid id, Guid complementoCceId, string fraccionArancelaria, string unidadAduana,
        decimal cantidadAduana, decimal valorUnitarioAduana, decimal valorDolares, bool aplicaIva0)
    {
        Id = id;
        ComplementoCceId = complementoCceId;
        FraccionArancelaria = fraccionArancelaria;
        UnidadAduana = unidadAduana;
        CantidadAduana = cantidadAduana;
        ValorUnitarioAduana = valorUnitarioAduana;
        ValorDolares = valorDolares;
        AplicaIva0 = aplicaIva0;
    }
}
