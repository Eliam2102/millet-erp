using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Lineas;

/// <summary>
/// F1-MP-01 (E1, A4.1-b): medida de la hoja en la línea de OC. Artículo ligado a un producto A+W ⇒ exige variante;
/// artículo sin liga ⇒ la rechaza. Misma regla y mismos códigos que <c>RecepcionVariante.ResolverAsync</c> de Almacén.
/// </summary>
internal static class OcLineaMedida
{
    public static async Task<VarianteVidrioLectura?> ResolverAsync(
        bool articuloLigado, IVarianteVidrioReadPort? variantes, Guid articuloId, Guid? varianteId, CancellationToken ct)
    {
        if (!articuloLigado)
        {
            if (varianteId is not null)
                throw new BusinessRuleException("VARIANTE_NO_APLICA", "El artículo no maneja medidas de hoja: quita la medida de la línea.");
            return null;
        }
        if (varianteId is null)
            throw new BusinessRuleException("VARIANTE_VIDRIO_REQUERIDA", "Elige la medida de la hoja.");
        var v = await variantes!.ObtenerAsync(articuloId, varianteId.Value, ct);
        if (v.AreaHojaM2 is null)
            throw new BusinessRuleException("VARIANTE_SIN_MEDIDA", "La medida elegida no tiene ancho y alto en A+W; no se puede usar.");
        return v;
    }

    /// <summary>
    /// RQ de la app de Almacenes: la línea trae ancho y alto de la hoja (mm). La partida de la OC hereda esa medida del catálogo
    /// A+W, en cualquiera de las dos orientaciones. Sin puerto, sin medida o sin coincidencia: null (se elige al editar la partida).
    /// </summary>
    public static async Task<VarianteVidrioLectura?> DeRequisicionAsync(
        IVarianteVidrioReadPort? variantes, Guid articuloId, int? anchoMm, int? altoMm, CancellationToken ct)
    {
        if (variantes is null || anchoMm is not int a || altoMm is not int h) return null;
        var lista = await variantes.ListarPorArticuloAsync(articuloId, ct);
        return lista.Where(v => v.AreaHojaM2 is not null && ((v.AnchoMm == a && v.AltoMm == h) || (v.AnchoMm == h && v.AltoMm == a)))
            .OrderBy(v => v.AnchoMm == a ? 0 : 1).ThenBy(v => v.ClaveVariante).FirstOrDefault();
    }
}
