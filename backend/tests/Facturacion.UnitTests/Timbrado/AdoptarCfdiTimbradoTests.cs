using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.Timbrado.Adopcion;
using Millet.Facturacion.Application.Timbrado.Contingencias;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Xunit;

namespace Millet.Facturacion.UnitTests.Timbrado;

/// <summary>M-284 (86e3zr751): adoptar en el ERP un CFDI que sí timbró. Datos ficticios.</summary>
public sealed class AdoptarCfdiTimbradoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 21, 0, 0, TimeSpan.Zero);
    private const string Uuid = "5E2D6F33-0A4B-4C1D-9E8F-112233445566";
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly FakeIntegrationEventPublisher _eventos = new();

    private FacturacionDbContext Db() => new(new DbContextOptionsBuilder<FacturacionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(_empresa));

    /// <summary>Factura MER-A 15230 por 11,600.00 que quedó «Timbrado fallido · PAC_TIMEOUT» con su contingencia abierta.</summary>
    private async Task<FacturaVenta> FacturaSinRespuestaAsync(FacturacionDbContext db, string codigo = "PAC_TIMEOUT")
    {
        var f = FacturaVenta.CrearBorrador(_empresa, "MERA-15230", 15230, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "76120"),
            "PUE", "03", "MXN", null, 2026, 11, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "30171500", "Vidrio", "H87", 1m, 10000m, 0m, "02", 0.16m, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbradoFallido(codigo, "Sin confirmación del PAC (prueba).");
        db.FacturasVenta.Add(f);
        await ContingenciasTimbrado.AplicarIntentoAsync(db, f, codigo, "Sin confirmación del PAC (prueba).", false, Ahora, default);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return f;
    }

    private static string Xml(string folio = "15230", string total = "11600.00", string rfcEmisor = "MIL010101AAA",
        string tipo = "I", bool conTimbre = true, string uuid = Uuid, string sello = "SELLOCFD") => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital"
            Version="4.0" Serie="MERA" Folio="{folio}" Total="{total}" SubTotal="10000.00" Moneda="MXN" TipoDeComprobante="{tipo}" Sello="{sello}">
          <cfdi:Emisor Rfc="{rfcEmisor}" Nombre="MILLET" RegimenFiscal="601" />
          <cfdi:Receptor Rfc="AAA010101AAA" Nombre="CLIENTE" UsoCFDI="G03" />
          <cfdi:Complemento>
            {(conTimbre ? $"""<tfd:TimbreFiscalDigital Version="1.1" UUID="{uuid}" FechaTimbrado="2026-11-11T14:59:00" RfcProvCertif="SPR190613I52" SelloCFD="SELLOCFD" NoCertificadoSAT="00001000000505211329" SelloSAT="SELLOSAT" />""" : "")}
          </cfdi:Complemento>
        </cfdi:Comprobante>
        """;

    private AdoptarCfdiTimbradoHandler Handler(FacturacionDbContext db, ICfdiTimbradoPort? fiscal = null) =>
        new(db, new FakeSender(new ReservarFolioResponse("NC-1", 1, "")), fiscal ?? new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(), _eventos, ReceptorFiscalTestFactory.Crear(db),
            new FakeUserContext(Guid.NewGuid(), "Facturación prueba"), new FakeClock(Ahora));

    [Fact]
    public async Task Folio_distinto_se_rechaza_con_el_folio_del_XML_y_nada_cambia()
    {
        using var db = Db();
        var f = await FacturaSinRespuestaAsync(db);

        var act = () => Handler(db).Handle(new AdoptarCfdiTimbradoCommand(f.Id, Xml(folio: "15231")), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message
            .Should().Contain("El folio del XML es 15231; el comprobante es 15230.");
        db.ChangeTracker.Clear();
        (await db.FacturasVenta.SingleAsync()).Estado.Should().Be(EstadoTimbrado.TimbradoFallido);
        _eventos.Publicados.Should().BeEmpty();
    }

    [Fact]
    public async Task Total_distinto_se_rechaza_por_importe()
    {
        using var db = Db();
        var f = await FacturaSinRespuestaAsync(db);

        var act = () => Handler(db).Handle(new AdoptarCfdiTimbradoCommand(f.Id, Xml(total: "11601.00")), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message
            .Should().Contain("El total del XML es 11,601.00; el comprobante es 11,600.00.");
    }

    [Fact]
    public async Task XML_correcto_deja_la_factura_timbrada_regulariza_la_contingencia_y_publica_un_evento()
    {
        using var db = Db();
        var f = await FacturaSinRespuestaAsync(db);

        var r = await Handler(db).Handle(new AdoptarCfdiTimbradoCommand(f.Id, Xml()), default);

        r.Adoptado.Should().BeTrue();
        db.ChangeTracker.Clear();
        var factura = await db.FacturasVenta.SingleAsync();
        (factura.Estado, factura.Uuid).Should().Be((EstadoTimbrado.Timbrado, Uuid));
        (await db.ContingenciasTimbrado.SingleAsync()).Estado.Should().Be(EstadoContingenciaTimbrado.Regularizada);
        _eventos.Publicados.OfType<FacturaVentaTimbradaIntegrationEvent>().Should().ContainSingle();
        (await db.BitacorasIntentoTimbrado.CountAsync(b => b.ErrorCodigo == "CFDI_ADOPTADO")).Should().Be(1);
    }

    [Fact]
    public async Task Solo_revisar_devuelve_el_cotejo_sin_cambiar_nada()
    {
        using var db = Db();
        var f = await FacturaSinRespuestaAsync(db);

        var r = await Handler(db).Handle(new AdoptarCfdiTimbradoCommand(f.Id, Xml(rfcEmisor: "XXX010101XXX"), SoloRevisar: true), default);

        r.Adoptado.Should().BeFalse();
        r.Cotejo.Single(c => c.Campo == "RFC emisor").Should().Be(new CampoCotejado("RFC emisor", "MIL010101AAA", "XXX010101XXX", false));
        r.Cotejo.Where(c => c.Campo != "RFC emisor").Should().OnlyContain(c => c.Coincide);
        db.ChangeTracker.Clear();
        (await db.FacturasVenta.SingleAsync()).Estado.Should().Be(EstadoTimbrado.TimbradoFallido);
    }

    [Fact]
    public async Task Comprobante_ya_timbrado_se_rechaza_y_no_se_publica_otra_vez()
    {
        using var db = Db();
        var f = await FacturaSinRespuestaAsync(db);
        await Handler(db).Handle(new AdoptarCfdiTimbradoCommand(f.Id, Xml()), default);
        db.ChangeTracker.Clear();

        var act = () => Handler(db).Handle(new AdoptarCfdiTimbradoCommand(f.Id, Xml()), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Be("El comprobante ya está timbrado.");
        _eventos.Publicados.OfType<FacturaVentaTimbradaIntegrationEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Rechazo_del_PAC_no_se_adopta_porque_no_genero_CFDI()
    {
        using var db = Db();
        var f = await FacturaSinRespuestaAsync(db, codigo: "CFDI40102");

        var act = () => Handler(db).Handle(new AdoptarCfdiTimbradoCommand(f.Id, Xml()), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("COMPROBANTE_NO_ADOPTABLE");
    }

    [Theory]
    [InlineData(false, "CFDI_XML_INVALIDO")]
    [InlineData(true, "CFDI_XML_INVALIDO")]
    public async Task XML_sin_timbre_o_con_DTD_se_rechaza(bool conDtd, string codigo)
    {
        using var db = Db();
        var f = await FacturaSinRespuestaAsync(db);
        var xml = conDtd
            ? """<?xml version="1.0"?><!DOCTYPE x [<!ENTITY e SYSTEM "file:///etc/passwd">]><x>&e;</x>"""
            : Xml(conTimbre: false);

        var act = () => Handler(db).Handle(new AdoptarCfdiTimbradoCommand(f.Id, xml), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(codigo);
    }

    [Fact]
    public async Task UUID_de_otro_comprobante_se_rechaza()
    {
        using var db = Db();
        var otra = await FacturaSinRespuestaAsync(db);
        await Handler(db).Handle(new AdoptarCfdiTimbradoCommand(otra.Id, Xml()), default);
        db.ChangeTracker.Clear();
        var f = FacturaVenta.CrearBorrador(_empresa, "MERA-15231", 15231, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "76120"),
            "PUE", "03", "MXN", null, 2026, 11, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "30171500", "Vidrio", "H87", 1m, 10000m, 0m, "02", 0.16m, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        db.FacturasVenta.Add(f);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var act = () => Handler(db).Handle(new AdoptarCfdiTimbradoCommand(f.Id, Xml(folio: "15231")), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("CFDI_UUID_EN_USO");
    }

    [Fact]
    public async Task O1_timbre_de_otro_CFDI_se_rechaza_aunque_los_datos_coincidan()
    {
        using var db = Db();
        var f = await FacturaSinRespuestaAsync(db);

        var act = () => Handler(db).Handle(new AdoptarCfdiTimbradoCommand(f.Id, Xml(sello: "SELLODEOTROCFDI")), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message
            .Should().Contain("El Timbre Fiscal Digital del XML no corresponde al sello del comprobante");
        db.ChangeTracker.Clear();
        (await db.FacturasVenta.SingleAsync()).Estado.Should().Be(EstadoTimbrado.TimbradoFallido);
    }

    [Theory]
    [InlineData(false, "El SAT no reporta este CFDI como vigente (estatus: No Encontrado)")]
    [InlineData(true, "No se pudo verificar el CFDI ante el SAT")]
    public async Task O1_CFDI_no_vigente_o_sin_respuesta_del_SAT_se_rechaza(bool consultaFalla, string mensaje)
    {
        using var db = Db();
        var f = await FacturaSinRespuestaAsync(db);
        var sat = new SatEstatus(consultaFalla);

        var act = () => Handler(db, sat).Handle(new AdoptarCfdiTimbradoCommand(f.Id, Xml()), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain(mensaje);
        sat.Consulta.Should().NotBeNull();
        (sat.Consulta!.Uuid, sat.Consulta.RfcEmisor, sat.Consulta.RfcReceptor, sat.Consulta.Total, sat.Consulta.Sello8)
            .Should().Be((Uuid, "MIL010101AAA", "AAA010101AAA", 11600m, "SELLOCFD"[^8..]));
        _eventos.Publicados.Should().BeEmpty();
    }

    /// <summary>SAT que no encuentra el CFDI (o cuya consulta falla).</summary>
    private sealed class SatEstatus(bool falla) : ICfdiTimbradoPort
    {
        public EstatusCfdiSolicitud? Consulta { get; private set; }
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) => throw new NotSupportedException();
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) => throw new NotSupportedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct)
        {
            Consulta = s;
            return falla
                ? throw new InvalidOperationException("servicio del SAT sin respuesta (simulado)")
                : Task.FromResult(new EstatusCfdiResultado(false, "No Encontrado", null, null));
        }
    }
}
