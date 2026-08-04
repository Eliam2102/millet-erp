using Millet.Facturacion.Application.Facturas.Queries;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Facturas;

/// <summary>FAC-UX-PR1 — defaults del emisor para el formulario de emisión.</summary>
public sealed class EmisorDefaultsHandlerTests
{
    [Fact]
    public async Task Devuelve_datos_fiscales_y_sucursal_unica()
    {
        var empresaId = Guid.NewGuid();
        var sucursalId = Guid.NewGuid();
        var handler = new EmisorDefaultsHandler(
            new FakeEmpresaFiscalReadPort(
                new EmpresaFiscalLectura(empresaId, "MIL010101ABC", "Millet SA de CV", "601", 0.16m, "76120"),
                sucursalId),
            new FakeEmpresaContext(empresaId));

        var resp = await handler.Handle(new EmisorDefaultsQuery(), CancellationToken.None);

        resp.RfcEmisor.Should().Be("MIL010101ABC");
        resp.RazonSocialEmisor.Should().Be("Millet SA de CV");
        resp.RegimenFiscalEmisor.Should().Be("601");
        resp.SucursalIdDefault.Should().Be(sucursalId);
        resp.TasaIvaDefault.Should().Be(0.16m);
    }

    [Fact]
    public async Task Sin_sucursal_unica_devuelve_null_y_el_usuario_elige()
    {
        var empresaId = Guid.NewGuid();
        var handler = new EmisorDefaultsHandler(
            new FakeEmpresaFiscalReadPort(
                new EmpresaFiscalLectura(empresaId, "MIL010101ABC", "Millet SA de CV", "601", null, null)),
            new FakeEmpresaContext(empresaId));

        var resp = await handler.Handle(new EmisorDefaultsQuery(), CancellationToken.None);

        resp.SucursalIdDefault.Should().BeNull();
        resp.TasaIvaDefault.Should().BeNull();
    }

    [Fact]
    public async Task Empresa_inexistente_lanza_NotFound()
    {
        var handler = new EmisorDefaultsHandler(
            new FakeEmpresaFiscalReadPort(),
            new FakeEmpresaContext(Guid.NewGuid()));

        var act = () => handler.Handle(new EmisorDefaultsQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "EMPRESA_NO_ENCONTRADA");
    }

    [Fact]
    public async Task Sin_empresa_en_contexto_lanza_Forbidden()
    {
        var handler = new EmisorDefaultsHandler(
            new FakeEmpresaFiscalReadPort(),
            new FakeEmpresaContext(null));

        var act = () => handler.Handle(new EmisorDefaultsQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>().Where(e => e.Code == "EMPRESA_NO_SELECCIONADA");
    }
}
