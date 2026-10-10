using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.PublicAdapters;
namespace Millet.Almacen.UnitTests.TestSupport;
internal static class P7Support
{
    public static ApartadosRequisicionService Apartados(AlmacenDbContext db) => new(db, new AlmacenSaldoQueryAdapter(db));
    public sealed class Conversion(decimal factorCaja = 12) : IConversionUnidadPort
    {
        public Task<ConversionUnidad> ConvertirAsync(Guid articuloId, decimal cantidad, string? unidadCapturada, string unidadDocumento, CancellationToken ct)
        {
            var capturada = string.IsNullOrWhiteSpace(unidadCapturada) ? unidadDocumento : unidadCapturada;
            if (string.IsNullOrWhiteSpace(capturada)) capturada = "PZA";
            var f = capturada == "CAJA" ? factorCaja : 1;
            var d = unidadDocumento == "CAJA" ? factorCaja : 1;
            return Task.FromResult(new ConversionUnidad(ConversionUnidades.Convertir(cantidad, f, 1), "PZA", ConversionUnidades.Convertir(cantidad, f, d), d, capturada, cantidad));
        }
    }
}
