using Millet.Compras.Application.Oc;
using Millet.Compras.Application.Oc.ActualizarCabecera;
using Millet.Compras.Application.Oc.CrearOrdenCompraVacia;
using Millet.Compras.Application.Oc.CrearOrdenCompraDesdeRequisicion;
using Millet.Compras.Domain.Ports.Catalogos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Application;

public sealed class DatosFacturaValidatorTests
{
    [Theory]
    [InlineData(null, null, null, true)]
    [InlineData("", "", "", true)]
    [InlineData("G03", "PPD", "99", true)]
    [InlineData("CP01", "PUE", "03", true)]
    [InlineData("G03", "XXX", "99", false)]
    [InlineData("ABCDE", "PUE", "03", false)]
    [InlineData("G03", "PUE", "XX", false)]
    public void Crear_y_editar_validan_datos_opcionales(string? uso, string? metodo, string? forma, bool valido)
    {
        var crear = new CrearOrdenCompraVaciaCommand(Guid.NewGuid(), "MID", 2026, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), new DateOnly(2026, 10, 20), UsoCfdiClave: uso, MetodoPago: metodo, FormaPagoClave: forma);
        var editar = new ActualizarCabeceraOcCommand(Guid.NewGuid(), null, null, null, null, null, null,
            null, null, null, null, null, null, null, null, UsoCfdiClave: uso, MetodoPago: metodo, FormaPagoClave: forma);
        var desdeRq = new CrearOrdenCompraDesdeRequisicionCommand(Guid.NewGuid(), "MID", 2026, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 20),
            UsoCfdiClave: uso, MetodoPago: metodo, FormaPagoClave: forma);
        Assert.Equal(valido, new CrearOrdenCompraDesdeRequisicionValidator().Validate(desdeRq).IsValid);
        Assert.Equal(valido, new CrearOrdenCompraVaciaValidator().Validate(crear).IsValid);
        Assert.Equal(valido, new ActualizarCabeceraOcValidator().Validate(editar).IsValid);
    }

    [Theory]
    [InlineData(false, true, "OC_USO_CFDI_INVALIDO")]
    [InlineData(true, false, "OC_FORMA_PAGO_INVALIDA")]
    [InlineData(null, true, "OC_USO_CFDI_INVALIDO")]
    [InlineData(true, null, "OC_FORMA_PAGO_INVALIDA")]
    public async Task Catalogo_rechaza_inactivos_e_inexistentes(bool? uso, bool? forma, string codigo)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            DatosFacturaOcGuard.ValidarAsync(new Catalogo(uso, forma), "G03", "99", CancellationToken.None));
        Assert.Equal(codigo, error.Code);
    }

    [Fact]
    public async Task Catalogo_admite_borrador_y_claves_activas()
    {
        await DatosFacturaOcGuard.ValidarAsync(new Catalogo(null, null), null, "", CancellationToken.None);
        await DatosFacturaOcGuard.ValidarAsync(new Catalogo(true, true), "G03", "99", CancellationToken.None);
    }

    private sealed class Catalogo(bool? uso, bool? forma) : ICatalogoSatReadPort
    {
        public Task<ClaveSatLectura?> ObtenerUsoCfdiAsync(string clave, CancellationToken cancellationToken) =>
            Task.FromResult(uso is bool activa ? new ClaveSatLectura("Uso ficticio", activa) : null);
        public Task<ClaveSatLectura?> ObtenerFormaPagoAsync(string clave, CancellationToken cancellationToken) =>
            Task.FromResult(forma is bool activa ? new ClaveSatLectura("Forma ficticia", activa) : null);
    }
}
