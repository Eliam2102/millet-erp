using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.UnitTests.PublicAdapters;

/// <summary>
/// Tests del <see cref="CxpDocumentosReadAdapter"/> — primer puerto de
/// lectura Almacén → CxP (ADR-0042). Verifica el contrato batch de los tres
/// resolvedores (ids distintos → una consulta; ids inexistentes ausentes del
/// diccionario; lista vacía no consulta) y el formateo serie-folio.
/// </summary>
public class CxpDocumentosReadAdapterTests
{
    [Fact]
    public async Task ObtenerFoliosFactura_formatea_serie_folio_e_ignora_inexistentes()
    {
        await using var db = NuevaDb();
        var factura = CrearFactura(folio: "1234", serie: "A");
        db.FacturasProveedor.Add(factura);
        await db.SaveChangesAsync();

        var adapter = new CxpDocumentosReadAdapter(db, new BypassedEmpresaContext());

        var folios = await adapter.ObtenerFoliosFacturaAsync(
            new[] { factura.Id, Guid.NewGuid() }, CancellationToken.None);

        folios.Should().HaveCount(1);
        folios[factura.Id].Should().Be("A-1234");
    }

    [Fact]
    public async Task ObtenerFoliosFactura_omite_facturas_sin_folio_del_proveedor()
    {
        await using var db = NuevaDb();
        var sinFolio = CrearFactura(folio: null, serie: null);
        db.FacturasProveedor.Add(sinFolio);
        await db.SaveChangesAsync();

        var adapter = new CxpDocumentosReadAdapter(db, new BypassedEmpresaContext());

        var folios = await adapter.ObtenerFoliosFacturaAsync(
            new[] { sinFolio.Id }, CancellationToken.None);

        // Ausente del diccionario → el consumidor cae al id truncado.
        folios.Should().BeEmpty();
    }

    [Fact]
    public async Task ObtenerUuidsFiscales_resuelve_el_folio_fiscal_del_SAT()
    {
        await using var db = NuevaDb();
        var cfdi = CrearCfdi("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B");
        db.CfdisRecibidos.Add(cfdi);
        await db.SaveChangesAsync();

        var adapter = new CxpDocumentosReadAdapter(db, new BypassedEmpresaContext());

        var uuids = await adapter.ObtenerUuidsFiscalesCfdiAsync(
            new[] { cfdi.Id, cfdi.Id, Guid.NewGuid() }, CancellationToken.None);

        // Ids duplicados no truenan el ToDictionary (Distinct interno).
        uuids.Should().HaveCount(1);
        uuids[cfdi.Id].Should().Be("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B");
    }

    [Fact]
    public async Task ObtenerFoliosNotaCredito_formatea_serie_folio()
    {
        await using var db = NuevaDb();
        var nc = CrearNotaCredito(folio: "77", serie: "NC-B");
        db.NotasCreditoProveedor.Add(nc);
        await db.SaveChangesAsync();

        var adapter = new CxpDocumentosReadAdapter(db, new BypassedEmpresaContext());

        var folios = await adapter.ObtenerFoliosNotaCreditoAsync(
            new[] { nc.Id }, CancellationToken.None);

        folios[nc.Id].Should().Be("NC-B-77");
    }

    [Fact]
    public async Task Listas_vacias_devuelven_diccionario_vacio_sin_consultar()
    {
        await using var db = NuevaDb();
        var adapter = new CxpDocumentosReadAdapter(db, new BypassedEmpresaContext());

        (await adapter.ObtenerFoliosFacturaAsync(Array.Empty<Guid>(), CancellationToken.None))
            .Should().BeEmpty();
        (await adapter.ObtenerUuidsFiscalesCfdiAsync(Array.Empty<Guid>(), CancellationToken.None))
            .Should().BeEmpty();
        (await adapter.ObtenerFoliosNotaCreditoAsync(Array.Empty<Guid>(), CancellationToken.None))
            .Should().BeEmpty();
    }

    // ─── Infra de test ───

    private static CuentasPorPagarDbContext NuevaDb()
    {
        var options = new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase($"cxp-documentos-adapter-{Guid.NewGuid():N}")
            .Options;
        return new CuentasPorPagarDbContext(options, new BypassedEmpresaContext());
    }

    private static Domain.FacturaProveedor.FacturaProveedor CrearFactura(
        string? folio, string? serie) =>
        Domain.FacturaProveedor.FacturaProveedor.CapturarConOc(
            empresaId: Guid.NewGuid(),
            cfdiRecibidoId: null,
            uuidCfdi: null,
            proveedorId: Guid.NewGuid(),
            sucursalId: Guid.NewGuid(),
            folioProveedor: folio, serieProveedor: serie,
            fechaDocumento: DateTimeOffset.UtcNow,
            fechaContabilizacion: DateTimeOffset.UtcNow,
            fechaVencimiento: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            moneda: "MXN", tipoCambio: null,
            subtotal: 1000m, descuentos: 0m,
            impuestosTrasladados: 160m, retenciones: 0m, total: 1160m,
            ordenCompraId: Guid.NewGuid(),
            encargadoComprasSnapshot: null,
            tolerancia: Tolerancia.MontoAbsoluto(0.99m),
            diferenciaContraOc: 0m,
            redondeoAplicado: 0m,
            ahora: DateTimeOffset.UtcNow);

    private static CfdiRecibido CrearCfdi(string uuid) =>
        CfdiRecibido.Ingresar(
            empresaId: Guid.NewGuid(),
            uuid: UuidCfdi.Parse(uuid),
            rfcEmisor: RfcMexicano.Parse("PRO010101AAA"),
            rfcReceptor: RfcMexicano.Parse("MIL010101AAA"),
            tipo: TipoCfdi.Ingreso,
            folio: "F1", serie: "A",
            fechaCfdi: DateTimeOffset.UtcNow,
            total: 1160m, subtotal: 1000m,
            impuestosTrasladados: 160m, retenciones: 0m,
            moneda: "MXN", tipoCambio: null,
            canalOrigen: CanalOrigenCfdi.CargaManual,
            fechaRecepcion: DateTimeOffset.UtcNow,
            xmlBlobRef: "blob://xml", pdfBlobRef: null,
            xmlHashSha256: "hash");

    private static Domain.NotaCreditoProveedor.NotaCreditoProveedor CrearNotaCredito(
        string? folio, string? serie) =>
        Domain.NotaCreditoProveedor.NotaCreditoProveedor.Capturar(
            empresaId: Guid.NewGuid(),
            cfdiRecibidoId: null,
            uuidCfdi: "6AB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B",
            proveedorId: Guid.NewGuid(),
            folioProveedor: folio, serieProveedor: serie,
            fechaCfdi: DateTimeOffset.UtcNow,
            moneda: "MXN", tipoCambio: null,
            subtotal: 100m, impuestosTrasladados: 16m,
            retenciones: 0m, total: 116m,
            tipo: TipoNotaCredito.Devolucion,
            tipoRelacionCfdi: TipoRelacionCfdi.Devolucion,
            uuidRelacionCfdi: "7CB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B",
            facturaOrigenId: null,
            capturadoPor: null,
            ahora: DateTimeOffset.UtcNow);

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
