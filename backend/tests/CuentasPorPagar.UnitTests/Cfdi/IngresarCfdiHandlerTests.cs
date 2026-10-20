using System.Text;
using Microsoft.EntityFrameworkCore;
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
        var handler = new IngresarCfdiHandler(db, new XmlCfdiParser(), new BlobStub(), empresa, new PeriodoAbiertoStub(), new EventosStub());

        await handler.Handle(new IngresarCfdiCommand(Xml(), null, CanalOrigenCfdi.CargaManual), default);
        var segunda = () => handler.Handle(new IngresarCfdiCommand(Xml(), null, CanalOrigenCfdi.CargaManual), default);

        var error = (await segunda.Should().ThrowAsync<BusinessRuleException>()).Which;
        error.Code.Should().Be("CFDI_DUPLICADO");
        error.Message.Should().Contain(Uuid).And.Contain("ya fue cargado antes");
    }

    private static MemoryStream Xml() => new(Encoding.UTF8.GetBytes($"""
        <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" TipoDeComprobante="I" Fecha="2026-10-09T12:00:00" Moneda="MXN" SubTotal="1000.00" Total="1160.00">
        <cfdi:Emisor Rfc="PROV010101AAA" Nombre="Proveedor ficticio"/><cfdi:Receptor Rfc="MIL010101AAA" Nombre="Empresa ficticia"/>
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
