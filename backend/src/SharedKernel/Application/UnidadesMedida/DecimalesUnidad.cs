namespace Millet.SharedKernel.Application.UnidadesMedida;

/// <summary>
/// Validador puro (sin I/O) de la regla de decimales por unidad
/// (ADR-0046 Etapa 2). Una cantidad es válida para una unidad si cabe
/// exactamente en los decimales que la unidad permite.
/// </summary>
public static class DecimalesUnidad
{
    /// <summary>
    /// Código de error de negocio (Problem Details, ADR-0010) cuando una
    /// cantidad tiene más decimales que los que su unidad permite.
    /// </summary>
    public const string CodigoError = "CANTIDAD_DECIMALES_EXCEDE_UNIDAD";

    /// <summary>
    /// <c>true</c> si <paramref name="cantidad"/> cabe en
    /// <paramref name="decimales"/> posiciones decimales. Test exacto por
    /// redondeo-igualdad: si redondear al número de decimales permitido no
    /// cambia el valor, la cantidad cabe. Robusto ante ceros a la derecha
    /// (<c>1.50</c> cabe en 1 decimal porque <c>1.50 == 1.5</c>) sin contar
    /// caracteres de la representación string.
    /// </summary>
    /// <param name="cantidad">Cantidad capturada (se asume &gt; 0; el rango
    /// lo valida FluentValidation antes del handler).</param>
    /// <param name="decimales">Decimales permitidos por la unidad (0..6,
    /// CHECK del catálogo).</param>
    public static bool EsValida(decimal cantidad, int decimales)
        => decimal.Round(cantidad, decimales, MidpointRounding.ToEven) == cantidad;
}
