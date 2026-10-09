using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.Application.UnidadesMedida;

public sealed record ConversionUnidad(decimal CantidadBase, string UnidadBase,
    decimal CantidadDocumento, decimal FactorDocumentoABase, string UnidadCapturada, decimal CantidadCapturada, int DecimalesDocumento = 4);

public interface IConversionUnidadPort
{
    Task<ConversionUnidad> ConvertirAsync(Guid articuloId, decimal cantidad,
        string? unidadCapturada, string unidadDocumento, CancellationToken ct);
}

public static class ConversionUnidades
{
    public static decimal Convertir(decimal cantidad, decimal factorOrigen, decimal factorDestino)
    {
        if (cantidad <= 0 || factorOrigen <= 0 || factorDestino <= 0)
            throw new BusinessRuleException("UNIDAD_CONVERSION_INVALIDA", "La cantidad y los factores de conversión deben ser positivos.");
        return checked(cantidad * factorOrigen / factorDestino);
    }
}
