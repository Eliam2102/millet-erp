using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Cfdi.ListarCfdis;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.UnitTests.Cfdi;

public sealed class ListarCfdisParaRecepcionTests
{
    private const string AvisoSinRfc = "El proveedor no tiene RFC registrado; captura el folio fiscal (UUID del impreso).";
    private const string AvisoGenerico = "El proveedor tiene RFC genérico; captura el folio fiscal (UUID del impreso).";
    private const string AvisoInvalido = "El RFC del proveedor no tiene un formato válido; corrígelo en el catálogo o captura el folio fiscal (UUID del impreso).";

    // Ramas de aviso: regresan antes de tocar la BD, por eso el DbContext es null.
    [Theory]
    [InlineData(null, AvisoSinRfc)]
    [InlineData("", AvisoSinRfc)]
    [InlineData("XAXX010101000", AvisoGenerico)]
    [InlineData("xexx010101000 ", AvisoGenerico)]
    [InlineData("XYZ", AvisoInvalido)]
    public async Task Rfc_no_utilizable_devuelve_aviso_sin_items(string? rfc, string aviso)
    {
        var handler = new ListarCfdisParaRecepcionHandler(null!);

        var respuesta = await handler.Handle(new ListarCfdisParaRecepcionQuery(rfc), CancellationToken.None);

        respuesta.Aviso.Should().Be(aviso);
        respuesta.RfcEmisor.Should().BeNull();
        respuesta.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Rfc_valido_devuelve_solo_cfdi_por_procesar_ingreso_del_rfc()
    {
        var options = new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase($"cxp-recepcion-cfdis-{Guid.NewGuid():N}")
            .Options;
        using var db = new CuentasPorPagarDbContext(options, new BypassedEmpresaContext(), new P8.PeriodoAbiertoStub(), new P8.PeriodoAbiertoStub());
        var empresa = Guid.NewGuid();
        var propio = Ingresar(empresa, "PROV010101AAA", "A1B2C3D4-0001-4000-8000-000000000001", TipoCfdi.Ingreso);
        var descartado = Ingresar(empresa, "PROV010101AAA", "A1B2C3D4-0002-4000-8000-000000000002", TipoCfdi.Ingreso);
        descartado.Descartar("duplicado manual");
        var otroRfc = Ingresar(empresa, "OTRO010101AAA", "A1B2C3D4-0003-4000-8000-000000000003", TipoCfdi.Ingreso);
        db.CfdisRecibidos.AddRange(propio, descartado, otroRfc);
        await db.SaveChangesAsync();

        var handler = new ListarCfdisParaRecepcionHandler(db);

        var respuesta = await handler.Handle(new ListarCfdisParaRecepcionQuery(" prov010101aaa "), CancellationToken.None);

        respuesta.Aviso.Should().BeNull();
        respuesta.RfcEmisor.Should().Be("PROV010101AAA");
        respuesta.Items.Should().ContainSingle().Which.Id.Should().Be(propio.Id);
    }

    private static CfdiRecibido Ingresar(Guid empresaId, string rfcEmisor, string uuid, TipoCfdi tipo) =>
        CfdiRecibido.Ingresar(
            empresaId: empresaId,
            uuid: UuidCfdi.Parse(uuid),
            rfcEmisor: RfcMexicano.Parse(rfcEmisor),
            rfcReceptor: RfcMexicano.Parse("MIL010101AAA"),
            tipo: tipo,
            folio: "F123",
            serie: "A",
            fechaCfdi: DateTimeOffset.UtcNow,
            total: 1160m,
            subtotal: 1000m,
            impuestosTrasladados: 160m,
            retenciones: 0m,
            moneda: "MXN",
            tipoCambio: null,
            canalOrigen: CanalOrigenCfdi.CargaManual,
            fechaRecepcion: DateTimeOffset.UtcNow,
            xmlBlobRef: "cxp/2026/05/cfdi/abc.xml",
            pdfBlobRef: null,
            xmlHashSha256: "deadbeef" + new string('0', 56));

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
