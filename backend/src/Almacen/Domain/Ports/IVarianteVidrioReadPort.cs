using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia Datos Maestros para la variante de medida del vidrio crudo (E07 D3).
/// Sin FK entre esquemas (ADR-0030): la integridad se valida aquí, al capturar la línea.
/// </summary>
public interface IVarianteVidrioReadPort
{
    /// <summary>
    /// Variante del artículo. Falla con <c>VARIANTE_NO_PERTENECE_AL_ARTICULO</c> (el artículo no está ligado a
    /// un producto A+W, o la variante es de otro producto o no existe) o <c>VARIANTE_INACTIVA</c> (producto
    /// A+W dado de baja: bloquea uso nuevo).
    /// </summary>
    Task<VarianteVidrioLectura> ObtenerAsync(Guid articuloId, Guid varianteId, CancellationToken cancellationToken);

    /// <summary>Variantes del producto A+W del artículo (vacío si no está ligado o el producto está inactivo).</summary>
    Task<IReadOnlyList<VarianteVidrioLectura>> ListarPorArticuloAsync(Guid articuloId, CancellationToken cancellationToken);
}

public sealed record VarianteVidrioLectura(
    Guid VarianteId,
    string ClaveVariante,
    decimal? AnchoMm,
    decimal? AltoMm,
    decimal? EspesorMm)
{
    /// <summary>m² por hoja de esta variante; null si A+W no informó alguna medida.</summary>
    public decimal? AreaHojaM2 => AnchoMm is { } a && AltoMm is { } h ? AreaHoja.M2(a, h) : null;
}

/// <summary>Factor hoja → m² (E07 D6, SUPUESTO por validar con Eliam): se redondea por hoja a 4 decimales.</summary>
public static class AreaHoja
{
    public static decimal M2(decimal anchoMm, decimal altoMm)
    {
        if (anchoMm <= 0 || altoMm <= 0)
            throw new BusinessRuleException("MEDIDA_HOJA_INVALIDA", "El ancho y el alto de la hoja deben ser mayores a 0.");
        return Math.Round(anchoMm * altoMm / 1_000_000m, 4, MidpointRounding.AwayFromZero);
    }
}
