namespace Millet.Facturacion.Domain.Facturas;

/// <summary>
/// M-216: el pedimento de la factura sale del expediente de Comercio exterior. Sólo se copia un número
/// capturado que quepa en la línea de la factura (21 caracteres, formato «15  47  3807  5000123»).
/// </summary>
public static class PedimentoExpediente
{
    public const int LongitudMaxima = 21;

    public static bool Aplicable(string? numero) =>
        !string.IsNullOrWhiteSpace(numero) && numero.Trim().Length <= LongitudMaxima;
}
