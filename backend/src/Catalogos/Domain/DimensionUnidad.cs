namespace Millet.Catalogos.Domain;

/// <summary>
/// Dimensión física de una <see cref="UnidadMedida"/> (ADR-0046). Cada
/// dimensión tiene UNA unidad base (<c>EsBase = true</c>, <c>FactorABase = 1</c>)
/// y el resto de unidades de la misma dimensión se convierten a la base con
/// su <c>FactorABase</c>. La conversión entre dimensiones distintas NO existe
/// (no se convierte peso a volumen).
///
/// <para>El empaque variable (cubeta, caja, barril, tambo) NO es una dimensión
/// del catálogo: vive en el artículo como factor propio (etapa futura), porque
/// su equivalencia depende del producto, no de una constante universal.</para>
/// </summary>
public enum DimensionUnidad : short
{
    /// <summary>Conteo discreto (base PZA). Ej. PZA, PAR.</summary>
    Conteo = 0,

    /// <summary>Peso (base KG). Ej. KG, G.</summary>
    Peso = 1,

    /// <summary>Volumen (base L). Ej. L, ML.</summary>
    Volumen = 2,

    /// <summary>Longitud (base M). Ej. M, CM, MM.</summary>
    Longitud = 3,

    /// <summary>Tiempo (base HR). Ej. HR.</summary>
    Tiempo = 4,
}
