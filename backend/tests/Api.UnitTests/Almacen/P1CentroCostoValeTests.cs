using Millet.Api.Adapters;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.UnitTests.Almacen;

public sealed class P1CentroCostoValeTests
{
    [Theory]
    [InlineData(Dim3Elegibilidad.Inactiva)]
    [InlineData(Dim3Elegibilidad.FueraDeAlcance)]
    [InlineData(Dim3Elegibilidad.NoExiste)]
    public async Task Vale_rechaza_ceco_no_elegible_y_exige_alcance(Dim3Elegibilidad estado)
    {
        var port = new Dim3Port(estado);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new AlmacenCentroCostoElegibilidadAdapter(port).ValidarAsync(Guid.NewGuid(), default));
        Assert.Equal("CECO_INVALIDO", error.Code);
        Assert.True(port.AplicaAlcance);
    }
    [Fact]
    public async Task Vale_admite_ceco_activo_dentro_del_alcance()
    {
        var port = new Dim3Port(Dim3Elegibilidad.Valida);
        await new AlmacenCentroCostoElegibilidadAdapter(port).ValidarAsync(Guid.NewGuid(), default);
        Assert.True(port.AplicaAlcance);
    }
    private sealed class Dim3Port(Dim3Elegibilidad estado) : IDim3ElegibilidadPort
    {
        public bool AplicaAlcance { get; private set; }
        public Task<Dim3Elegibilidad> EvaluarAsync(Guid dim3Id, bool aplicarAlcance, CancellationToken ct)
        {
            AplicaAlcance = aplicarAlcance;
            return Task.FromResult(estado);
        }
    }
}
