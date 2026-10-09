using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

public sealed class ConversionUnidadAdapter(CompartidoDbContext db) : IConversionUnidadPort
{
    public async Task<ConversionUnidad> ConvertirAsync(Guid articuloId, decimal cantidad,
        string? unidadCapturada, string unidadDocumento, CancellationToken ct)
    {
        if (cantidad <= 0)
            throw new BusinessRuleException("UNIDAD_CONVERSION_INVALIDA", "La cantidad debe ser positiva.");
        var articulo = await db.Articulos.AsNoTracking().FirstOrDefaultAsync(a => a.Id == articuloId, ct)
            ?? throw new EntityNotFoundException("ARTICULO_NO_ENCONTRADO", "El artículo no existe en el catálogo.");
        var codigoBase = articulo.UnidadMedidaDefault;
        if (string.IsNullOrWhiteSpace(unidadDocumento)) unidadDocumento = codigoBase;
        var capturada = string.IsNullOrWhiteSpace(unidadCapturada) ? unidadDocumento : unidadCapturada.Trim().ToUpperInvariant();
        // Legacy sin unidad catalogada: sólo identidad, nunca adivinar equivalencias.
        if (articulo.UnidadMedidaId is null && capturada == codigoBase && unidadDocumento == codigoBase)
            return new(cantidad, codigoBase, cantidad, 1m, capturada, cantidad);
        var codigos = new[] { codigoBase, capturada, unidadDocumento };
        var unidades = await db.UnidadesMedida.AsNoTracking().Where(u => codigos.Contains(u.Codigo)).ToDictionaryAsync(u => u.Codigo, ct);
        if (!unidades.TryGetValue(codigoBase, out var baseArt) || !unidades.TryGetValue(capturada, out var origen)
            || !unidades.TryGetValue(unidadDocumento, out var documento))
            throw new BusinessRuleException("UNIDAD_EQUIVALENCIA_REQUERIDA", "Configura en el catálogo la unidad del artículo, la capturada y su equivalencia antes de registrar.");
        if (origen.Estatus != EstatusCatalogo.Activo || baseArt.Estatus != EstatusCatalogo.Activo || documento.Estatus != EstatusCatalogo.Activo)
            throw new BusinessRuleException("UNIDAD_INACTIVA", "Activa la unidad de medida en el catálogo antes de registrar el movimiento.");
        if (origen.Dimension != baseArt.Dimension || documento.Dimension != baseArt.Dimension)
            throw new BusinessRuleException("UNIDAD_DIMENSION_INCOMPATIBLE", "Las unidades deben pertenecer a la misma dimensión para convertirlas.");
        var enBase = ConversionUnidades.Convertir(cantidad, origen.FactorABase, baseArt.FactorABase);
        var enDocumento = ConversionUnidades.Convertir(cantidad, origen.FactorABase, documento.FactorABase);
        if (!DecimalesUnidad.EsValida(cantidad, origen.Decimales) || !DecimalesUnidad.EsValida(enBase, baseArt.Decimales)
            || !DecimalesUnidad.EsValida(enDocumento, documento.Decimales) || decimal.Round(enBase, 4) != enBase)
            throw new BusinessRuleException("CANTIDAD_DECIMALES_EXCEDE_UNIDAD", "La cantidad convertida no cabe en los decimales permitidos; corrige la cantidad o la equivalencia.");
        return new(enBase, codigoBase, enDocumento, documento.FactorABase / baseArt.FactorABase, capturada, cantidad, documento.Decimales);
    }
}
