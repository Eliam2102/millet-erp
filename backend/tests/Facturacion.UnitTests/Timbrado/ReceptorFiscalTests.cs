using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Timbrado;

public sealed class ReceptorFiscalTests
{
    private static DatosFiscalesReceptor Receptor => new("AAA010101AAA", "Cliente prueba", "601", "97000", "G03", "MEX", false);
    private static DatosFiscalesEmisor Emisor => new("MIL010101AAA", "Millet", "601", "97000");

    [Theory]
    [InlineData("G01", "601", true)]
    [InlineData("G03", "605", false)]
    [InlineData("I08", "626", true)]
    [InlineData("D01", "605", true)]
    [InlineData("D10", "601", false)]
    [InlineData("S01", "616", true)]
    [InlineData("CP01", "616", true)]
    [InlineData("CN01", "605", true)]
    [InlineData("CN01", "601", false)]
    [InlineData("P01", "601", false)]
    [InlineData("G03", "999", false)]
    [InlineData("G02", "616", true)]
    [InlineData("G01", "616", false)]
    public void Compatibilidad_respeta_matriz(string uso, string regimen, bool esperado) =>
        CompatibilidadUsoRegimen.EsCompatible(uso, regimen).Should().Be(esperado);

    [Theory]
    [InlineData("", "601", "codigoPostalFiscal")]
    [InlineData("1234", "601", "codigoPostalFiscal")]
    [InlineData("97000", "", "regimenFiscal")]
    [InlineData("97000", "605", "usoCfdi")]
    public void CA2_6_CA6_4_U19b(string cp, string regimen, string campo) =>
        ValidacionReceptorFiscal.Validar(Receptor with { CodigoPostal = cp, RegimenFiscal = regimen }, "97000", true, true)
            .Should().Contain(e => e.Campo == campo);

    [Fact]
    public void CN01_se_rechaza_por_ser_exclusivo_de_nomina() =>
        ValidacionReceptorFiscal.Validar(Receptor with { Rfc = "DEMO010101AB1", RegimenFiscal = "605", UsoCfdi = "CN01" }, "97000", true, true)
            .Should().ContainSingle(e => e.Campo == "usoCfdi" && e.Motivo.Contains("nómina"));

    [Theory]
    [InlineData("AAA010101AAA", "605", "S01")]   // moral con régimen exclusivo de física
    [InlineData("DEMO010101AB1", "601", "S01")]  // física con régimen exclusivo de moral
    public void Regimen_debe_corresponder_al_tipo_de_persona(string rfc, string regimen, string uso) =>
        ValidacionReceptorFiscal.Validar(Receptor with { Rfc = rfc, RegimenFiscal = regimen, UsoCfdi = uso }, "97000", true, true)
            .Should().ContainSingle(e => e.Campo == "regimenFiscal");

    [Fact]
    public void Persona_moral_no_puede_usar_deducciones_personales() =>
        ValidacionReceptorFiscal.Validar(Receptor with { UsoCfdi = "D01" }, "97000", true, true)
            .Should().Contain(e => e.Campo == "usoCfdi" && e.Motivo.Contains("personas físicas"));

    [Theory]
    [InlineData("AAA010101AAA")]
    [InlineData("DEMO010101AB1")]
    public void Regimen_626_aplica_a_ambas_personas(string rfc) =>
        ValidacionReceptorFiscal.Validar(Receptor with { Rfc = rfc, RegimenFiscal = "626", UsoCfdi = "G03" }, "97000", true, true)
            .Should().BeEmpty();

    [Fact]
    public void G02_con_616_para_persona_fisica_pasa() =>
        ValidacionReceptorFiscal.Validar(Receptor with { Rfc = "DEMO010101AB1", RegimenFiscal = "616", UsoCfdi = "G02" }, "97000", true, true)
            .Should().BeEmpty();

    [Fact]
    public void U19a_publico_general_valido_pasa() =>
        ValidacionReceptorFiscal.Validar(Receptor with { Rfc = "XAXX010101000", RegimenFiscal = "616", UsoCfdi = "S01" }, "97000", true, true)
            .Should().BeEmpty();

    [Fact]
    public void Generico_rechaza_regimen_uso_y_cp_incorrectos() =>
        ValidacionReceptorFiscal.Validar(Receptor with { Rfc = "XAXX010101000" }, "76120", true, true)
            .Select(e => e.Campo).Should().Contain(["regimenFiscal", "usoCfdi", "codigoPostalFiscal"]);

    [Fact]
    public void Acumula_faltantes_y_formato_rfc() =>
        ValidacionReceptorFiscal.Validar(Receptor with { Rfc = "INVALIDO", Nombre = " ", RegimenFiscal = "", CodigoPostal = "" }, "97000", false, true)
            .Select(e => e.Campo).Should().Contain(["rfc", "razonSocial", "regimenFiscal", "codigoPostalFiscal"]);

    [Fact]
    public void Comprueba_existencia_en_catalogos() =>
        ValidacionReceptorFiscal.Validar(Receptor, "97000", false, false)
            .Select(e => e.Campo).Should().BeEquivalentTo(["regimenFiscal", "usoCfdi"]);

    [Theory]
    [InlineData(false, null, null, true)]
    [InlineData(true, null, null, false)]
    [InlineData(true, "TAX123", "USA", true)]
    [InlineData(true, "TAX123", "MEX", false)]
    public void Extranjero_exige_identidad_para_cce(bool cce, string? identificacion, string? pais, bool valido) =>
        ValidacionReceptorFiscal.Validar(Receptor with { Rfc = "XEXX010101000", RegimenFiscal = "616", UsoCfdi = "S01", Pais = "USA" },
            "97000", true, true, exportacionConCce: cce, numRegIdTrib: identificacion, paisResidencia: pais)
            .Any().Should().Be(!valido);

    [Fact]
    public void Extranjero_no_admite_MEX() =>
        ValidacionReceptorFiscal.Validar(Receptor with { Rfc = "XEXX010101000", RegimenFiscal = "616", UsoCfdi = "S01" }, "97000", true, true)
            .Should().Contain(e => e.Campo == "paisResidencia");

    [Theory]
    [InlineData("")]
    [InlineData("MEXICO")]
    public void Conserva_validacion_del_pais_del_receptor(string pais) =>
        ValidacionReceptorFiscal.Validar(Receptor with { Pais = pais }, "97000", true, true)
            .Should().Contain(e => e.Campo == "paisResidencia");

    [Fact]
    public void Rep_valida_CP01_en_lugar_del_uso_original() =>
        // 605 es de persona física (c_RegimenFiscal): RFC de 13 caracteres.
        ValidacionReceptorFiscal.Validar(Receptor with { Rfc = "DEMO010101AB1", RegimenFiscal = "605", UsoCfdi = "G03" }, "97000", true, true, esRep: true)
            .Should().BeEmpty();

    [Theory]
    [InlineData(null, "601", null, "codigoPostalFiscal")]
    [InlineData("97000", null, null, "regimenFiscal")]
    [InlineData("97000", "601", "BBB010101BBB", "rfc")]
    [InlineData("97000", "601", "", "rfc")]
    [InlineData("76120", "601", null, "codigoPostalFiscal")]
    [InlineData("97000", "626", null, "regimenFiscal")]
    public async Task Maestro_incompleto_o_distinto_bloquea_sin_folio_ni_pac(string? cp, string? regimen, string? rfc, string campo)
    {
        var empresaId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        using var db = new FacturacionDbContext(new DbContextOptionsBuilder<FacturacionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(empresaId));
        var clientes = new FakeClientesReadPort(new ClienteFiscalLectura(clienteId, rfc ?? Receptor.Rfc,
            Receptor.Nombre, regimen, cp, "G03", "01", "PUE", "MXN", false));
        var catalogos = new FakeCatalogosSatReadPort();
        var sender = new FakeSender(new ReservarFolioResponse("F-1", 1, ""));
        var pac = new FakeFiscalApiClient();
        var handler = new EmitirFacturaVentaHandler(db, sender, new FakePeriodoContablePort(), catalogos,
            pac, new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, Emisor.Rfc, Emisor.Nombre, Emisor.RegimenFiscal, 0.16m, Emisor.LugarExpedicion)),
            new FakeIntegrationEventPublisher(), new FakeContabilidadAsientoPort(), new FakeEmpresaContext(empresaId),
            new FakeUserContext(Guid.NewGuid()), new FakeClock(DateTimeOffset.UtcNow), new ValidadorReceptorFiscal(clientes, catalogos, db));
        var command = new EmitirFacturaVentaCommand(Guid.NewGuid(), Receptor.Rfc, Receptor.Nombre, Receptor.RegimenFiscal,
            Receptor.CodigoPostal, Receptor.UsoCfdi, Receptor.Pais, Emisor.Rfc, Emisor.RegimenFiscal, "PUE", "01", "MXN", null,
            1, ComportamientoFiscal.Administrativa, null, null, false,
            [new(null, "01010101", "Prueba", "H87", 1m, 100m, 0m, "02", 0.16m, null, null)], ClienteId: clienteId);

        var error = await Assert.ThrowsAsync<ReceptorFiscalInvalidoException>(() => handler.Handle(command, CancellationToken.None));
        error.Campos.Should().Contain(e => e.Campo == campo);
        error.ClienteId.Should().Be(clienteId);
        error.EnlaceCliente.Should().Be($"/admin/datos-maestros/clientes/{clienteId}");
        sender.VecesReservoFolio.Should().Be(0);
        pac.UltimaEmision.Should().BeNull();
        db.Comprobantes.Should().BeEmpty();
        db.BitacorasIntentoTimbrado.Should().BeEmpty();
    }
    [Fact]
    public async Task Maestro_compara_sin_espacios_ni_mayusculas_y_lista_todos_los_faltantes()
    {
        using var db = new FacturacionDbContext(new DbContextOptionsBuilder<FacturacionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(Guid.NewGuid()));
        var clienteId = Guid.NewGuid();
        var completo = new ClienteFiscalLectura(clienteId, " aaa010101aaa ", "Cliente", " 601 ", "97 000", null, null, null, "MXN", false);
        await new ValidadorReceptorFiscal(new FakeClientesReadPort(completo), new FakeCatalogosSatReadPort(), db)
            .ValidarAsync(Receptor, Emisor, clienteId, CancellationToken.None);
        var incompleto = completo with { Rfc = null, RazonSocial = "", RegimenFiscal = null, CodigoPostalFiscal = null };
        var error = await Assert.ThrowsAsync<ReceptorFiscalInvalidoException>(() =>
            new ValidadorReceptorFiscal(new FakeClientesReadPort(incompleto), new FakeCatalogosSatReadPort(), db)
                .ValidarAsync(Receptor, Emisor, clienteId, CancellationToken.None));
        error.Campos.Select(c => c.Campo).Should().BeEquivalentTo(["rfc", "razonSocial", "regimenFiscal", "codigoPostalFiscal"]);
    }

}
