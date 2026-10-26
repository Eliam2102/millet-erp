using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.Application.Recepciones;

/// <summary>
/// F1-MP-01 S3 (E07 D3/D5/D6): medida de la hoja en la recepción (E1: la trae la línea de la OC, no se captura). Artículo ligado a un producto A+W ⇒ exige variante;
/// artículo sin liga ⇒ la rechaza. La variante se valida contra el producto vigente del artículo (re-valida la liga y
/// que el producto siga activo). Captura en HOJA ⇒ m² = hojas × m² por hoja (D6, SUPUESTO por validar con Eliam).
/// </summary>
internal static class RecepcionVariante
{
    public const string UnidadHoja = "HOJA";
    public const string MensajeHojasEnteras = "Con medida, captura la cantidad en hojas enteras.";

    /// <summary>
    /// F1-MP-01 S4 (Eliam E3): el lote del proveedor es obligatorio para el artículo ligado a A+W (el que resuelve una variante)
    /// y opcional para el resto. Devuelve el lote normalizado (recortado; vacío = NULL).
    /// </summary>
    public static string? ExigirLote(VarianteVidrioLectura? variante, string? lote)
    {
        var normalizado = Domain.Movimientos.LineaMovimiento.NormalizarLote(lote);
        if (variante is not null && normalizado is null)
            throw new BusinessRuleException("LOTE_REQUERIDO", "Captura el lote del proveedor.");
        return normalizado;
    }

    /// <summary>La cantidad en m² de una línea con medida debe ser un número entero de hojas (m² = hojas × m² por hoja).</summary>
    public static void ExigirHojasEnteras(decimal cantidadM2, decimal? areaHojaM2)
    {
        if (areaHojaM2 is not { } area || cantidadM2 % area != 0)
            throw new BusinessRuleException("HOJAS_ENTERAS", MensajeHojasEnteras);
    }

    /// <summary>Salida de una medida sin hojas suficientes en el rack: nombra la medida en vez del mensaje genérico del bin.</summary>
    public static void ExigirExistencia(VarianteVidrioLectura? medida, decimal disponibleM2, decimal solicitadoM2)
    {
        if (medida?.AreaHojaM2 is not { } area || disponibleM2 >= solicitadoM2) return;
        throw new BusinessRuleException("SALIDA_STOCK_INSUFICIENTE",
            $"No hay hojas suficientes de {medida.AnchoMm:0.##} × {medida.AltoMm:0.##} mm en el rack: "
            + $"hay {Math.Floor(disponibleM2 / area):0.##} y pides {solicitadoM2 / area:0.##}.");
    }

    /// <summary>
    /// Cantidad (m²) de una línea ya existente para el guard de decimales: con medida exige hojas enteras y la marca derivada
    /// (los m² no los capturó la persona); sin medida se valida normal.
    /// </summary>
    public static CantidadAValidar Cantidad(
        Guid articuloId, decimal cantidadM2, string? unidad, Guid? varianteId, decimal? anchoMm, decimal? altoMm)
    {
        if (varianteId is null) return new CantidadAValidar(articuloId, cantidadM2, unidad);
        ExigirHojasEnteras(cantidadM2, anchoMm is { } a && altoMm is { } h ? AreaHoja.M2(a, h) : null);
        return new CantidadAValidar(articuloId, cantidadM2, unidad, Derivada: true);
    }

    public static async Task<VarianteVidrioLectura?> ResolverAsync(
        IArticuloReadPort? articulos, IVarianteVidrioReadPort? variantes, Guid articuloId, Guid? varianteId, CancellationToken ct,
        bool nuevoUso = true)
    {
        // Sin los puertos (solo pruebas unitarias que no manejan medidas) no hay artículos ligados.
        var ligado = articulos is not null && (await articulos.ObtenerAsync(articuloId, ct))?.ProductoAwId is not null;
        if (!ligado)
        {
            if (varianteId is not null)
                throw new BusinessRuleException("VARIANTE_NO_APLICA", "El artículo no maneja medidas de hoja: quita la medida de la línea.");
            return null;
        }
        if (varianteId is null)
            throw new BusinessRuleException("VARIANTE_VIDRIO_REQUERIDA",
                nuevoUso ? "La línea de la OC no tiene medida; corrígela en Compras." : "Elige la medida de la hoja.");
        // Recibir es uso nuevo (producto activo y liga vigente). Sacar o mover existencia ya recibida no lo es: la fila de saldo
        // de esa medida es la que manda (el trigger rechaza una medida sin existencia), aunque el producto se haya dado de baja.
        var v = nuevoUso
            ? await variantes!.ObtenerAsync(articuloId, varianteId.Value, ct)
            : (await variantes!.ObtenerPorIdsAsync([varianteId.Value], ct)).GetValueOrDefault(varianteId.Value)
                ?? throw new BusinessRuleException("VARIANTE_NO_PERTENECE_AL_ARTICULO", "La medida elegida no existe.");
        if (v.AreaHojaM2 is null)
            throw new BusinessRuleException("VARIANTE_SIN_MEDIDA", "La medida elegida no tiene ancho y alto en A+W; no se puede recibir.");
        return v;
    }

    public static async Task<ConversionUnidad> ConvertirAsync(
        IConversionUnidadPort conversion, Guid articuloId, decimal cantidad, string? unidadCapturada,
        string unidadDocumento, VarianteVidrioLectura? variante, CancellationToken ct)
    {
        if (variante is null)
            return await conversion.ConvertirAsync(articuloId, cantidad, unidadCapturada, unidadDocumento, ct);
        if (!string.Equals(unidadCapturada?.Trim(), UnidadHoja, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("HOJAS_ENTERAS", MensajeHojasEnteras);
        if (cantidad <= 0 || cantidad != decimal.Truncate(cantidad))
            throw new BusinessRuleException("HOJAS_ENTERAS", "Las hojas se capturan en números enteros mayores a 0.");
        // El documento (OC, RQ) de vidrio por medida va en la unidad de existencia (m²); en hojas necesitaría factor por medida.
        var documento = await conversion.ConvertirAsync(articuloId, 1m, null, unidadDocumento, ct);
        if (documento.FactorDocumentoABase != 1m)
            throw new BusinessRuleException("RECEPCION_UNIDAD_OC_VIDRIO",
                "La línea del documento de vidrio por medida debe estar en la unidad del artículo (m²).");
        var m2 = cantidad * variante.AreaHojaM2!.Value;
        return new ConversionUnidad(m2, documento.UnidadBase, m2, 1m, UnidadHoja, cantidad, documento.DecimalesDocumento);
    }
}
