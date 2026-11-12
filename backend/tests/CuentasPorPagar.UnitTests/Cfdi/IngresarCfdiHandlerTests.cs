using System.Text;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Application.Ports;
using Millet.CuentasPorPagar.Application.Cfdi.IngresarCfdi;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Parsing;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.UnitTests.P8;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.Cfdi;

public sealed class IngresarCfdiHandlerTests
{
    private const string Uuid = "5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B";

    [Fact]
    public async Task Ingresar_dos_veces_el_mismo_XML_rechaza_con_CFDI_DUPLICADO_y_menciona_el_UUID()
    {
        var empresa = new EmpresaContext(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase($"cxp-ingresar-cfdi-{Guid.NewGuid():N}").Options;
        using var db = new CuentasPorPagarDbContext(options, empresa, new PeriodoAbiertoStub(), new PeriodoAbiertoStub());
        var handler = new IngresarCfdiHandler(db, new XmlCfdiParser(), new BlobStub(), empresa, new PeriodoAbiertoStub(), new EventosStub(),
            new EmpresaPorRfc(empresa.Current!.Value));

        await handler.Handle(new IngresarCfdiCommand(Xml(), null, CanalOrigenCfdi.CargaManual), default);
        var segunda = () => handler.Handle(new IngresarCfdiCommand(Xml(), null, CanalOrigenCfdi.CargaManual), default);

        var error = (await segunda.Should().ThrowAsync<BusinessRuleException>()).Which;
        error.Code.Should().Be("CFDI_DUPLICADO");
        error.Message.Should().Contain(Uuid).And.Contain("ya fue cargado antes");
    }

    // ---- M-257: el RFC receptor de la carga manual debe ser el de la empresa ----

    private sealed class EmpresaPorRfc(Guid empresaId, string rfc = "MIL010101AAA", Guid? otraEmpresaId = null, string? rfcOtra = null) : IEmpresaResolverPort
    {
        public Task<EmpresaResolution?> ResolveByRfcAsync(string rfcBuscado, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmpresaResolution?>(
                rfcBuscado.Equals(rfc, StringComparison.OrdinalIgnoreCase) ? new(empresaId, rfc, "Empresa ficticia")
                : otraEmpresaId is Guid o && rfcBuscado.Equals(rfcOtra, StringComparison.OrdinalIgnoreCase) ? new(o, rfcOtra!, "Otra empresa ficticia")
                : null);
    }

    private static (CuentasPorPagarDbContext Db, IngresarCfdiHandler Handler, BlobStub Blob) Armar(Guid? otraEmpresa = null, string? rfcOtra = null)
    {
        var empresa = new EmpresaContext(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase($"cxp-ingresar-cfdi-rfc-{Guid.NewGuid():N}").Options;
        var db = new CuentasPorPagarDbContext(options, empresa, new PeriodoAbiertoStub(), new PeriodoAbiertoStub());
        var blob = new BlobStub();
        var handler = new IngresarCfdiHandler(db, new XmlCfdiParser(), blob, empresa, new PeriodoAbiertoStub(), new EventosStub(),
            new EmpresaPorRfc(empresa.Current!.Value, otraEmpresaId: otraEmpresa, rfcOtra: rfcOtra));
        return (db, handler, blob);
    }

    [Fact]
    public async Task Un_XML_emitido_a_la_empresa_se_acepta_en_la_carga_manual()
    {
        var (db, handler, _) = Armar();
        using var _db = db;

        var r = await handler.Handle(new IngresarCfdiCommand(Xml(), null, CanalOrigenCfdi.CargaManual), default);

        r.UuidCfdi.Should().Be(Uuid);
        (await db.CfdisRecibidos.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Un_XML_emitido_a_otro_RFC_se_rechaza_con_mensaje_claro_y_no_crea_el_CFDI()
    {
        var (db, handler, _) = Armar();
        using var _db = db;

        var error = (await ((Func<Task>)(() => handler.Handle(
            new IngresarCfdiCommand(Xml(rfcReceptor: "OTR010101AAA"), null, CanalOrigenCfdi.CargaManual), default)))
            .Should().ThrowAsync<BusinessRuleException>()).Which;

        error.Code.Should().Be("CFDI_RFC_RECEPTOR_NO_COINCIDE");
        error.Message.Should().Contain("OTR010101AAA").And.Contain("no es el de la empresa").And.Contain("no se cargó");
        (await db.CfdisRecibidos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Un_XML_emitido_a_otra_empresa_del_grupo_se_rechaza_y_pide_cambiar_de_empresa()
    {
        var (db, handler, _) = Armar(otraEmpresa: Guid.NewGuid(), rfcOtra: "OTR010101AAA");
        using var _db = db;

        var error = (await ((Func<Task>)(() => handler.Handle(
            new IngresarCfdiCommand(Xml(rfcReceptor: "OTR010101AAA"), null, CanalOrigenCfdi.CargaManual), default)))
            .Should().ThrowAsync<BusinessRuleException>()).Which;

        error.Code.Should().Be("CFDI_RFC_RECEPTOR_NO_COINCIDE");
        error.Message.Should().Contain("otra empresa").And.Contain("Cambia de empresa");
        (await db.CfdisRecibidos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Los_canales_automaticos_con_empresa_resuelta_no_pasan_por_la_regla_de_carga_manual()
    {
        var (db, handler, _) = Armar();
        using var _db = db;

        // Buzón/SAT: la empresa ya se resolvió por el RFC receptor del XML (override), no se vuelve a validar aquí.
        var r = await handler.Handle(
            new IngresarCfdiCommand(Xml(rfcReceptor: "OTR010101AAA"), null, CanalOrigenCfdi.Mailbox, EmpresaIdOverride: Guid.NewGuid()), default);

        r.UuidCfdi.Should().Be(Uuid);
    }

    private static MemoryStream Xml(string rfcReceptor = "MIL010101AAA") => new(Encoding.UTF8.GetBytes($"""
        <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" TipoDeComprobante="I" Fecha="2026-10-09T12:00:00" Moneda="MXN" SubTotal="1000.00" Total="1160.00">
        <cfdi:Emisor Rfc="PROV010101AAA" Nombre="Proveedor ficticio"/><cfdi:Receptor Rfc="{rfcReceptor}" Nombre="Empresa ficticia"/>
        <cfdi:Conceptos><cfdi:Concepto ClaveProdServ="30102400" Cantidad="1" ClaveUnidad="H87" Descripcion="Material" ValorUnitario="1000.00" Importe="1000.00"/></cfdi:Conceptos>
        <cfdi:Impuestos TotalImpuestosTrasladados="160.00"/>
        <cfdi:Complemento><tfd:TimbreFiscalDigital UUID="{Uuid}"/></cfdi:Complemento></cfdi:Comprobante>
        """));

    private sealed class EmpresaContext(Guid id) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = id;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }

    private sealed class BlobStub : ICfdiBlobStorage
    {
        public Task<string> GuardarXmlAsync(string uuid, DateTimeOffset fecha, Stream contenido, CancellationToken ct) => Task.FromResult($"test/{uuid}.xml");
        public Task<string?> GuardarPdfAsync(string uuid, DateTimeOffset fecha, Stream? contenido, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<Stream?> LeerXmlAsync(string blobRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream?> LeerPdfAsync(string blobRef, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class EventosStub : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken ct) => Task.CompletedTask;
    }
}
