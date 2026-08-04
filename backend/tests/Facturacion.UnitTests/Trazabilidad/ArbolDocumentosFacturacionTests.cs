using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Trazabilidad;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Trazabilidad;

/// <summary>
/// Doc 13 §4.3 (13-D) — árbol de trazabilidad documento-céntrico: un nivel
/// de ascendientes/descendientes por raíz, resuelto desde FKs + relaciones
/// CFDI + AnticipoVinculacion. Se arma la cadena real (anticipo → factura
/// final → NC de amortización) con los handlers de emisión.
/// </summary>
public sealed class ArbolDocumentosFacturacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
    private const string RfcCliente = "AAA010101AAA";

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private sealed class FakeSenderFoliosUnicos : ISender
    {
        private int _n;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            if (request is ReservarFolioCommand)
            {
                _n++;
                return Task.FromResult((TResponse)(object)new ReservarFolioResponse($"DOC-{_n:D6}", _n, ""));
            }
            throw new NotImplementedException();
        }
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeFiscalUuidsUnicos : ICfdiTimbradoPort
    {
        private int _n;
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct)
        {
            _n++;
            return Task.FromResult(new TimbradoResultado(
                TimbradoEstado.Timbrado, $"00000000-0000-0000-0000-{_n:D12}", "S", "S", "0",
                Ahora, "SAT970701NN3", "<cfdi fake=\"true\" />", null, null));
        }
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) =>
            Task.FromResult(new CancelacionResultado(CancelacionEstado.Aceptada, "ok", null));
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            Task.FromResult(new EstatusCfdiResultado(true, "Vigente", true, null));
    }

    private static FakeEmpresaFiscalReadPort EmpresasFiscal(Guid empresaId) =>
        new(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120"));

    private static EmitirFacturaAnticipoHandler EmitirAnticipoHandler(
        FacturacionDbContext db, Guid empresaId, ISender sender, ICfdiTimbradoPort fiscal) =>
        new(db, sender, new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), fiscal,
            new FakeCfdiRepositorioPort(), EmpresasFiscal(empresaId), new FakeIntegrationEventPublisher(),
            new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    private static EmitirFacturaVentaHandler FacturaHandler(
        FacturacionDbContext db, Guid empresaId, ISender sender, ICfdiTimbradoPort fiscal) =>
        new(db, sender, new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), fiscal,
            new FakeCfdiRepositorioPort(), EmpresasFiscal(empresaId), new FakeIntegrationEventPublisher(),
            new FakeContabilidadAsientoPort(), new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    private static EmitirFacturaAnticipoCommand AnticipoCommand(decimal montoBase = 1000m) => new(
        Guid.NewGuid(), Guid.NewGuid(), RfcCliente, "Cliente Maquila", "601", "97000", "G03", "MEX",
        "BBB010101BBB", "601", "PUE", "03", "MXN", null, TipoAnticipo.ClientesMxp, montoBase, 0.16m, null, null, "AW-7", null, null);

    private static EmitirFacturaVentaCommand FacturaCommand(IReadOnlyList<AnticipoAAmortizar> anticipos) => new(
        SucursalId: Guid.NewGuid(),
        ReceptorRfc: RfcCliente, ReceptorNombre: "Cliente Maquila", ReceptorRegimenFiscal: "601",
        ReceptorCodigoPostal: "97000", ReceptorUsoCfdi: "G03", ReceptorPais: "MEX",
        RfcEmisor: "BBB010101BBB", RegimenFiscalEmisor: "601",
        MetodoPago: "PUE", FormaPago: "03", Moneda: "MXN", TipoCambio: null,
        CanalVenta: (short)7, ComportamientoFiscal: ComportamientoFiscal.ConAnticipo,
        ObraId: null, ObraNombre: null, FacturaAgrupada: false,
        Lineas: [new EmitirFacturaVentaLinea(null, "01010101", "Servicio maquila", "E48", 1m, 10000m, 0m, "02", 0.16m, null, null)],
        Anticipos: anticipos);

    private static ArbolDocumentosFacturacionHandler Handler(FacturacionDbContext db) =>
        new(db, new FakeAlcanceCajaEvaluator());

    /// <summary>Emite anticipo + factura final amortizando (cadena completa M1+M2+M3).</summary>
    private static async Task<(Guid FacturaAnticipoId, Guid FacturaVentaId, Guid NcId)> CadenaCompletaAsync(
        FacturacionDbContext db, Guid empresaId)
    {
        var sender = new FakeSenderFoliosUnicos();
        var fiscal = new FakeFiscalUuidsUnicos();
        var anticipo = await EmitirAnticipoHandler(db, empresaId, sender, fiscal)
            .Handle(AnticipoCommand(), CancellationToken.None);
        var factura = await FacturaHandler(db, empresaId, sender, fiscal)
            .Handle(FacturaCommand([new AnticipoAAmortizar(anticipo.AnticipoId, 500m)]), CancellationToken.None);
        var ncId = factura.NotasCreditoAmortizacion![0].Id;
        return (anticipo.FacturaAnticipoId, factura.Id, ncId);
    }

    [Fact]
    public async Task Raiz_factura_anticipo_desciende_a_factura_final_y_nc()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (faId, fvId, ncId) = await CadenaCompletaAsync(db, empresaId);

        var arbol = await Handler(db).Handle(
            new ArbolDocumentosFacturacionQuery(TipoNodoTrazabilidadFacturacion.FacturaAnticipo, faId),
            CancellationToken.None);

        arbol.Actual.Tipo.Should().Be((short)TipoNodoTrazabilidadFacturacion.FacturaAnticipo);
        arbol.Ascendientes.Should().BeEmpty(); // sin pedido de origen
        arbol.Descendientes.Should().HaveCount(2);
        arbol.Descendientes.Should().ContainSingle(n =>
            n.Tipo == (short)TipoNodoTrazabilidadFacturacion.FacturaVenta && n.Id == fvId);
        arbol.Descendientes.Should().ContainSingle(n =>
            n.Tipo == (short)TipoNodoTrazabilidadFacturacion.NotaCredito && n.Id == ncId);
    }

    [Fact]
    public async Task Raiz_factura_venta_asciende_al_anticipo_y_desciende_a_la_nc()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (faId, fvId, ncId) = await CadenaCompletaAsync(db, empresaId);

        var arbol = await Handler(db).Handle(
            new ArbolDocumentosFacturacionQuery(TipoNodoTrazabilidadFacturacion.FacturaVenta, fvId),
            CancellationToken.None);

        arbol.Actual.Id.Should().Be(fvId);
        arbol.Ascendientes.Should().ContainSingle(n =>
            n.Tipo == (short)TipoNodoTrazabilidadFacturacion.FacturaAnticipo && n.Id == faId);
        arbol.Descendientes.Should().ContainSingle(n =>
            n.Tipo == (short)TipoNodoTrazabilidadFacturacion.NotaCredito && n.Id == ncId);
    }

    [Fact]
    public async Task Raiz_nc_asciende_al_anticipo_y_a_la_factura_final()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (faId, fvId, ncId) = await CadenaCompletaAsync(db, empresaId);

        var arbol = await Handler(db).Handle(
            new ArbolDocumentosFacturacionQuery(TipoNodoTrazabilidadFacturacion.NotaCredito, ncId),
            CancellationToken.None);

        arbol.Actual.Tipo.Should().Be((short)TipoNodoTrazabilidadFacturacion.NotaCredito);
        arbol.Descendientes.Should().BeEmpty();
        arbol.Ascendientes.Should().HaveCount(2);
        arbol.Ascendientes.Should().ContainSingle(n => n.Id == faId);
        arbol.Ascendientes.Should().ContainSingle(n => n.Id == fvId);
    }

    [Fact]
    public async Task Comprobante_inexistente_lanza_404()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var act = () => Handler(db).Handle(
            new ArbolDocumentosFacturacionQuery(TipoNodoTrazabilidadFacturacion.FacturaVenta, Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>()
            .Where(e => e.Code == "COMPROBANTE_NO_ENCONTRADO");
    }

    [Fact]
    public async Task Factura_sin_relaciones_devuelve_arbol_solo_con_la_raiz()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var factura = await FacturaHandler(db, empresaId, sender, new FakeFiscalUuidsUnicos())
            .Handle(FacturaCommand([]), CancellationToken.None);

        var arbol = await Handler(db).Handle(
            new ArbolDocumentosFacturacionQuery(TipoNodoTrazabilidadFacturacion.FacturaVenta, factura.Id),
            CancellationToken.None);

        arbol.Ascendientes.Should().BeEmpty();
        arbol.Descendientes.Should().BeEmpty();
        arbol.Actual.Folio.Should().NotBeNullOrEmpty();
    }
}
