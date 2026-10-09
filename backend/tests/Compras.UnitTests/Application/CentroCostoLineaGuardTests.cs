using Millet.CentrosCosto.Application.PublicPorts;
using Millet.Compras.Application;
using Millet.SharedKernel.Application.Exceptions;
using Xunit;

namespace Millet.Compras.UnitTests.Application;

public class CentroCostoLineaGuardTests
{
    private sealed class FakeDim3ElegibilidadPort : IDim3ElegibilidadPort
    {
        public Dim3Elegibilidad Resultado { get; set; } = Dim3Elegibilidad.Valida;
        public Guid UltimoDim3Id { get; private set; }
        public bool UltimoAplicarAlcance { get; private set; }

        public Task<Dim3Elegibilidad> EvaluarAsync(
            Guid dim3Id, bool aplicarAlcance, CancellationToken cancellationToken)
        {
            UltimoDim3Id = dim3Id;
            UltimoAplicarAlcance = aplicarAlcance;
            return Task.FromResult(Resultado);
        }
    }

    [Fact]
    public async Task ValidarAsync_CuandoElegibilidadValida_NoLanzaExcepcion()
    {
        var dim3Id = Guid.NewGuid();
        var puerto = new FakeDim3ElegibilidadPort { Resultado = Dim3Elegibilidad.Valida };

        await CentroCostoLineaGuard.ValidarAsync(
            puerto, dim3Id, aplicarAlcance: true, CancellationToken.None);

        Assert.Equal(dim3Id, puerto.UltimoDim3Id);
        Assert.True(puerto.UltimoAplicarAlcance);
    }

    [Fact]
    public async Task ValidarAsync_CuandoNoExiste_LanzaCecoInvalido()
    {
        var dim3Id = Guid.NewGuid();
        var puerto = new FakeDim3ElegibilidadPort { Resultado = Dim3Elegibilidad.NoExiste };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CentroCostoLineaGuard.ValidarAsync(
                puerto, dim3Id, aplicarAlcance: true, CancellationToken.None));

        Assert.Equal("CECO_INVALIDO", ex.Code);
        Assert.Contains("el centro de costo no existe", ex.Message);
    }

    [Fact]
    public async Task ValidarAsync_CuandoInactiva_LanzaCecoInvalido()
    {
        var dim3Id = Guid.NewGuid();
        var puerto = new FakeDim3ElegibilidadPort { Resultado = Dim3Elegibilidad.Inactiva };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CentroCostoLineaGuard.ValidarAsync(
                puerto, dim3Id, aplicarAlcance: false, CancellationToken.None));

        Assert.Equal("CECO_INVALIDO", ex.Code);
        Assert.Contains("centro de costo inactivo", ex.Message);
        Assert.False(puerto.UltimoAplicarAlcance);
    }

    [Fact]
    public async Task ValidarAsync_CuandoFueraDeAlcance_LanzaCecoInvalido()
    {
        var dim3Id = Guid.NewGuid();
        var puerto = new FakeDim3ElegibilidadPort { Resultado = Dim3Elegibilidad.FueraDeAlcance };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CentroCostoLineaGuard.ValidarAsync(
                puerto, dim3Id, aplicarAlcance: true, CancellationToken.None));

        Assert.Equal("CECO_INVALIDO", ex.Code);
        Assert.Contains("centro de costo fuera de su alcance", ex.Message);
    }
}
