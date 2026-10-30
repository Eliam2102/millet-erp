using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Repp.EmitirRepp;
using Millet.Facturacion.Application.Repp.Queries;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Repp;

/// <summary>
/// 86e3tfdve — cuentas ordenante/beneficiaria del complemento de pago validadas
/// contra c_FormaPago. Todas las cuentas son ficticias.
/// </summary>
public sealed class ReppCuentasTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
    private const string Clabe18 = "000000000000000018";
    private const string Numero10 = "0000000010";

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(empresaId));

    private static FacturaVenta FacturaPpd(Guid empresaId)
    {
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente ficticio", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Emisor ficticio", "601", "76120"), "PPD", "99", "MXN", null, 2026, 5,
            (short)7, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "Servicio", "E48", 1m, 1000m, 0m, "02", 0m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado("11111111-1111-1111-1111-111111111111", null, null, null, Ahora, null, null);
        return fv;
    }

    private static EmitirReppHandler Handler(FacturacionDbContext db, Guid empresaId, FakeFiscalApiClient fiscal,
        params CuentaBancariaMillet[] cuentas) =>
        new(db, new FakeSender(new ReservarFolioResponse("P-000001", 1, "")), new FakePeriodoContablePort(), fiscal,
            new FakeCfdiRepositorioPort(), new FakeIntegrationEventPublisher(), new FakeEmpresaContext(empresaId),
            new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db),
            new FakeCuentaBancariaMilletReadPort(cuentas));

    // ---- Tabla SAT (dominio) ----

    [Theory]
    [InlineData("03", Clabe18, null)]
    [InlineData("03", "0000000000000016", Numero10)]
    [InlineData("02", "00000000011", "ABC_0000000001")]
    [InlineData("04", "0000000000000016", Clabe18)]
    [InlineData("06", Numero10, null)]
    [InlineData("29", "000000000000015", null)]
    [InlineData("01", null, null)]
    [InlineData("03", " 0000 0000 0000 000018 ", null)] // espacios de captura se normalizan
    public void Cuentas_validas_segun_c_FormaPago(string forma, string? ordenante, string? beneficiaria)
    {
        var act = () => CuentasPagoSat.Validar(forma, ordenante, beneficiaria);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("01", Numero10, null, "no admite cuenta ordenante")]
    [InlineData("03", "000000000012", null, "10, 16 o 18 dígitos")]
    [InlineData("03", null, "0000000000000016", "10 o 18 dígitos")]
    [InlineData("06", null, Numero10, "no admite cuenta beneficiaria")]
    [InlineData("04", "000000000000000018", null, "16 dígitos")]
    [InlineData("99", null, Clabe18, "no admite cuenta beneficiaria")]
    public void Cuenta_fuera_de_formato_es_REPP_CUENTA_INVALIDA(string forma, string? ordenante, string? beneficiaria, string esperado)
    {
        var act = () => CuentasPagoSat.Validar(forma, ordenante, beneficiaria);
        var ex = act.Should().Throw<BusinessRuleException>().Which;
        ex.Code.Should().Be("REPP_CUENTA_INVALIDA");
        ex.Message.Should().Contain(forma).And.Contain(esperado);
        // El mensaje nunca expone la cuenta completa (ADR-0006).
        if (ordenante is not null && ordenante.Length > 4) ex.Message.Should().NotContain(ordenante);
    }

    [Theory]
    [InlineData("03", Clabe18)]
    [InlineData("02", Clabe18)]
    [InlineData("06", null)]
    [InlineData("01", null)]
    public void Sugerencia_de_beneficiaria_prefiere_CLABE_si_la_forma_la_admite(string forma, string? esperada)
    {
        CuentasPagoSat.SugerirBeneficiaria(forma, Clabe18, "00000000001").Should().Be(esperada);
        // Sin CLABE, cae al número si cumple el patrón (03 admite 10 dígitos).
        CuentasPagoSat.SugerirBeneficiaria("03", null, Numero10).Should().Be(Numero10);
    }

    [Fact]
    public void Enmascarado_muestra_solo_los_ultimos_cuatro()
    {
        CuentasPagoSat.Enmascarar(Clabe18).Should().Be("•••• 0018");
        CuentasPagoSat.Enmascarar(null).Should().BeNull();
    }

    // ---- EmitirReppCommand (REP manual) ----

    [Fact]
    public async Task Forma_01_con_cuenta_ordenante_rechaza_sin_timbrar()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var f = FacturaPpd(empresa); db.FacturasVenta.Add(f); await db.SaveChangesAsync();
        var fiscal = new FakeFiscalApiClient();

        var act = () => Handler(db, empresa, fiscal).Handle(new EmitirReppCommand(
            f.SucursalId, Ahora, "MXN", null, "01", Numero10, null, null, [new ReppFacturaPago(f.Id, 100m)]), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("REPP_CUENTA_INVALIDA");
        fiscal.UltimaEmision.Should().BeNull();
        (await db.RecibosPago.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Forma_03_con_12_digitos_rechaza()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var f = FacturaPpd(empresa); db.FacturasVenta.Add(f); await db.SaveChangesAsync();
        var act = () => Handler(db, empresa, new FakeFiscalApiClient()).Handle(new EmitirReppCommand(
            f.SucursalId, Ahora, "MXN", null, "03", "000000000012", null, null, [new ReppFacturaPago(f.Id, 100m)]), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("REPP_CUENTA_INVALIDA");
    }

    [Fact]
    public async Task Forma_03_con_cuentas_validas_las_lleva_al_recibo_y_al_CFDI()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var f = FacturaPpd(empresa); db.FacturasVenta.Add(f); await db.SaveChangesAsync();
        var fiscal = new FakeFiscalApiClient();

        await Handler(db, empresa, fiscal).Handle(new EmitirReppCommand(
            f.SucursalId, Ahora, "MXN", null, "03", " 0000-0000-0000-0016 ", Clabe18, "SPEI-1",
            [new ReppFacturaPago(f.Id, 100m)]), default);

        var pago = fiscal.UltimaEmision!.ComplementoPago!;
        pago.CuentaOrdenante.Should().Be("0000000000000016");
        pago.CuentaBeneficiaria.Should().Be(Clabe18);
        var linea = (await db.RecibosPago.Include(r => r.FacturasPagadas).SingleAsync()).FacturasPagadas.Single();
        linea.CuentaOrdenante.Should().Be("0000000000000016");
        linea.CuentaBeneficiaria.Should().Be(Clabe18);
    }

    [Fact]
    public async Task Beneficiaria_por_id_de_cuenta_de_Millet_usa_la_CLABE()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var f = FacturaPpd(empresa); db.FacturasVenta.Add(f); await db.SaveChangesAsync();
        var fiscal = new FakeFiscalApiClient();
        var cuenta = new CuentaBancariaMillet(Guid.NewGuid(), "Banco ficticio", Numero10, Clabe18, "MXN", true);

        await Handler(db, empresa, fiscal, cuenta).Handle(new EmitirReppCommand(
            f.SucursalId, Ahora, "MXN", null, "03", null, null, null, [new ReppFacturaPago(f.Id, 100m)],
            CuentaBancariaBeneficiariaId: cuenta.Id), default);

        fiscal.UltimaEmision!.ComplementoPago!.CuentaBeneficiaria.Should().Be(Clabe18);
    }

    [Fact]
    public async Task Beneficiaria_por_id_con_forma_que_no_la_admite_rechaza()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var f = FacturaPpd(empresa); db.FacturasVenta.Add(f); await db.SaveChangesAsync();
        var cuenta = new CuentaBancariaMillet(Guid.NewGuid(), "Banco ficticio", Numero10, Clabe18, "MXN", true);
        var act = () => Handler(db, empresa, new FakeFiscalApiClient(), cuenta).Handle(new EmitirReppCommand(
            f.SucursalId, Ahora, "MXN", null, "01", null, null, null, [new ReppFacturaPago(f.Id, 100m)],
            CuentaBancariaBeneficiariaId: cuenta.Id), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("REPP_CUENTA_INVALIDA");
    }

    [Fact]
    public void Validador_limita_cuentas_a_50_caracteres()
    {
        var cmd = new EmitirReppCommand(Guid.NewGuid(), Ahora, "MXN", null, "05", new string('A', 51), null, null,
            [new ReppFacturaPago(Guid.NewGuid(), 1m)]);
        new EmitirReppValidator().Validate(cmd).Errors.Should().Contain(e => e.PropertyName == "CuentaOrdenante");
    }

    // ---- Builder ----

    [Fact]
    public void Builder_lleva_CtaOrdenante_y_CtaBeneficiario_al_complemento()
    {
        var repp = ReciboPago.CrearBorrador(Guid.NewGuid(), "P-1", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), 2026, 5, Ahora, "MXN");
        repp.AgregarFacturaPagada(Guid.NewGuid(), "UUID-1", 1, "MXN", 100m, 1000m, "03", null, null,
            "0000000000000016", Clabe18, "REF", "01", 1000m, []);
        repp.EstablecerImporteTotalPago(100m);
        var pago = CfdiEmisionBuilder.DesdeReciboPago(repp, Ahora).ComplementoPago!;
        pago.CuentaOrdenante.Should().Be("0000000000000016");
        pago.CuentaBeneficiaria.Should().Be(Clabe18);
    }

    // ---- Bandeja y detalle enmascarados ----

    [Fact]
    public async Task Detalle_y_bandeja_muestran_las_cuentas_enmascaradas()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var f = FacturaPpd(empresa); db.FacturasVenta.Add(f); await db.SaveChangesAsync();
        var r = await Handler(db, empresa, new FakeFiscalApiClient()).Handle(new EmitirReppCommand(
            f.SucursalId, Ahora, "MXN", null, "03", "0000000000000016", Clabe18, null, [new ReppFacturaPago(f.Id, 100m)]), default);

        var alcance = new FakeAlcanceCajaEvaluator();
        var detalle = await new ReppDetalleHandler(db, alcance).Handle(new ReppDetalleQuery(r.Id), default);
        detalle.FormaPago.Should().Be("03");
        detalle.CuentaOrdenante.Should().Be("•••• 0016");
        detalle.CuentaBeneficiaria.Should().Be("•••• 0018");
        var item = (await new BandejaReppHandler(db, alcance).Handle(new BandejaReppQuery(null, 0, 50), default)).Items.Single();
        item.CuentaOrdenante.Should().Be("•••• 0016");
        item.CuentaBeneficiaria.Should().Be("•••• 0018");
    }
}
