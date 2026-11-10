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

    /// <summary>XML timbrado y sellado de verdad (certificados ficticios); <paramref name="sello"/> pone otro SelloCFD en el timbre.</summary>
    private static string Xml(string folio = "15230", string total = "11600.00", string rfcEmisor = "MIL010101AAA",
        string tipo = "I", bool conTimbre = true, string uuid = Uuid, string? sello = null) =>
        CfdiFirmadoDePrueba.Xml(folio: folio, total: total, rfcEmisor: rfcEmisor, tipo: tipo, uuid: uuid,
            conTimbre: conTimbre, selloCfdTimbre: sello);

    private AdoptarCfdiTimbradoHandler Handler(FacturacionDbContext db, ICfdiTimbradoPort? fiscal = null,
        ICertificadoSatProvider? certificadosSat = null) =>
        new(db, new FakeSender(new ReservarFolioResponse("NC-1", 1, "")), fiscal ?? new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(), _eventos, ReceptorFiscalTestFactory.Crear(db),
            new FakeUserContext(Guid.NewGuid(), "Facturación prueba"), new FakeClock(Ahora),
            certificadosSat: certificadosSat ?? new CfdiFirmadoDePrueba.CertificadosSat());

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

        var act = () => Handler(db).Handle(new AdoptarCfdiTimbradoCommand(f.Id, Xml(sello: "U0VMTE9ERU9UUk9DRkRJ")), default);

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
        (sat.Consulta!.Uuid, sat.Consulta.RfcEmisor, sat.Consulta.RfcReceptor, sat.Consulta.Total)
            .Should().Be((Uuid, "MIL010101AAA", "AAA010101AAA", 11600m));
        sat.Consulta.Sello8.Should().HaveLength(8);
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

    [Fact]
    public async Task ReQA_O1_XML_con_el_folio_editado_despues_de_timbrar_se_rechaza_por_el_sello_del_emisor()
    {
        // El caso de la re-QA: el CFDI se timbró con otro folio y se editó para que coincida con el comprobante.
        using var db = Db();
        var f = await FacturaSinRespuestaAsync(db);
        var editado = Xml(folio: "18").Replace("Folio=\"18\"", "Folio=\"15230\"");

        var act = () => Handler(db).Handle(new AdoptarCfdiTimbradoCommand(f.Id, editado), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message
            .Should().Contain("El sello del emisor no corresponde al contenido del XML; el archivo fue modificado.");
        db.ChangeTracker.Clear();
        (await db.FacturasVenta.SingleAsync()).Estado.Should().Be(EstadoTimbrado.TimbradoFallido);
    }

    [Fact]
    public async Task ReQA_O1_concepto_editado_se_rechaza_y_el_cotejo_lo_muestra()
    {
        using var db = Db();
        var f = await FacturaSinRespuestaAsync(db);
        var editado = Xml().Replace("Descripcion=\"Vidrio\"", "Descripcion=\"Vidrio templado\"");

        var r = await Handler(db).Handle(new AdoptarCfdiTimbradoCommand(f.Id, editado, SoloRevisar: true), default);

        r.Cotejo.Single(c => c.Campo == "Sello del emisor").Coincide.Should().BeFalse();
        r.Cotejo.Where(c => c.Campo != "Sello del emisor").Should().OnlyContain(c => c.Coincide);
    }

    [Fact]
    public async Task ReQA_O1_timbre_alterado_se_rechaza_por_el_sello_del_SAT()
    {
        using var db = Db();
        var f = await FacturaSinRespuestaAsync(db);
        var alterado = Xml().Replace("FechaTimbrado=\"2026-11-11T14:59:00\"", "FechaTimbrado=\"2026-11-11T15:59:00\"");

        var act = () => Handler(db).Handle(new AdoptarCfdiTimbradoCommand(f.Id, alterado), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message
            .Should().Contain("El sello del SAT no corresponde al Timbre Fiscal Digital");
    }

    [Fact]
    public async Task ReQA_O1_sin_certificado_del_SAT_fuera_de_pruebas_no_se_adopta()
    {
        using var db = Db();
        var f = await FacturaSinRespuestaAsync(db);

        var act = () => Handler(db, certificadosSat: new CfdiFirmadoDePrueba.CertificadosSat(disponible: false))
            .Handle(new AdoptarCfdiTimbradoCommand(f.Id, Xml()), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message
            .Should().Contain("No se pudo obtener el certificado del SAT");
    }

    [Fact]
    public void ReQA_O1_el_verificador_acepta_el_XML_valido_y_rechaza_el_editado()
    {
        var valido = Xml();

        VerificadorSelloCfdi.VerificarSelloEmisor(valido).Valido.Should().BeTrue();
        VerificadorSelloCfdi.VerificarSelloEmisor(valido.Replace("Total=\"11600.00\"", "Total=\"11700.00\"")).Valido.Should().BeFalse();
        VerificadorSelloCfdi.CadenaOriginal(valido).Should().StartWith("||4.0|MERA|15230|2026-11-11T14:58:00|03|" + CfdiFirmadoDePrueba.NoCertificadoEmisor + "|");
    }
}
