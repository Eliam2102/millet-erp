using FluentAssertions;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Timbrado;

public sealed class EmisorSnapshotTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Resolver_reutiliza_los_datos_fiscales_de_Empresa()
    {
        var lectura = new EmpresaFiscalLectura(
            EmpresaId, "AAA010101AAA", "Emisor SA de CV", "601", 0.16m, "76120");

        var result = await EmisorSnapshot.ResolverAsync(
            new FakeEmpresaFiscalReadPort(lectura), EmpresaId, CancellationToken.None);

        result.Rfc.Should().Be(lectura.Rfc);
        result.Nombre.Should().Be(lectura.RazonSocial);
        result.RegimenFiscal.Should().Be(lectura.RegimenFiscal);
        result.LugarExpedicion.Should().Be(lectura.CodigoPostal);
    }

    [Fact]
    public async Task Resolver_sin_codigo_postal_da_mensaje_accionable()
    {
        var lectura = new EmpresaFiscalLectura(
            EmpresaId, "AAA010101AAA", "Emisor SA de CV", "601", null, null);

        var act = () => EmisorSnapshot.ResolverAsync(
            new FakeEmpresaFiscalReadPort(lectura), EmpresaId, CancellationToken.None);

        var error = await act.Should().ThrowAsync<BusinessRuleException>();
        error.Which.Code.Should().Be("EMISOR_SIN_LUGAR_EXPEDICION");
        error.Which.Message.Should().Contain("Administración → Empresas");
    }
}
