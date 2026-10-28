using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Conteos;

/// <summary>
/// F1-MP-01 S5: el vidrio por medida se cuenta en hojas, pero el saldo, la teórica y el ajuste viven en m². Las líneas con
/// medida reciben hojas enteras y se convierten con el m² por hoja de su medida (D6, SUPUESTO por validar con Eliam).
/// </summary>
internal static class ConteoHojas
{
    public static async Task<decimal> AM2Async(
        IVarianteVidrioReadPort? variantes, LineaConteo linea, decimal capturado, CancellationToken ct)
    {
        if (linea.ProductoAwVarianteId is not Guid id) return capturado;
        if (capturado != decimal.Truncate(capturado))
            throw new BusinessRuleException("HOJAS_ENTERAS", "Las hojas se cuentan en números enteros.");
        var medida = variantes is null ? null : (await variantes.ObtenerPorIdsAsync([id], ct)).GetValueOrDefault(id);
        return capturado * (medida?.AreaHojaM2
            ?? throw new BusinessRuleException("VARIANTE_SIN_MEDIDA", "La medida de la línea no tiene ancho y alto en A+W; no se puede contar en hojas."));
    }
}
