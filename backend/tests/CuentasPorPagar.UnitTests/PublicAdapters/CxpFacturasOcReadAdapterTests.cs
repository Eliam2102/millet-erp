using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.UnitTests.PublicAdapters;

/// <summary>Facturas por OC para el reporte de proveedores: filtra por OC, conserva diferencia y estado.</summary>
public class CxpFacturasOcReadAdapterTests
{
    [Fact]
    public async Task Devuelve_facturas_de_la_oc_con_diferencia_y_estado_sin_las_de_otra_oc()
    {
        await using var db = NuevaDb();
        var oc = Guid.NewGuid();
        var conDiferencia = CrearFactura(oc, "F-1", diferencia: 0.5m);
        var cancelada = CrearFactura(oc, "F-2", diferencia: 0m);
        cancelada.Cancelar(MotivoCancelacion.ErrorCaptura, "prueba", Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.FacturasProveedor.AddRange(conDiferencia, cancelada, CrearFactura(Guid.NewGuid(), "F-3", 0m));
        await db.SaveChangesAsync();

        var filas = await new CxpFacturasOcReadAdapter(db).ListarPorOcAsync([oc], CancellationToken.None);

        filas.Should().HaveCount(2);
        var f1 = filas.Single(f => f.Folio == "F-1");
        f1.FacturaId.Should().Be(conDiferencia.Id);
        f1.OcId.Should().Be(oc);
        f1.Serie.Should().Be("A");
        f1.Moneda.Should().Be("MXN");
        f1.Total.Should().Be(1160m);
        f1.DiferenciaContraOc.Should().Be(0.5m);
        f1.Estado.Should().Be(nameof(EstadoPasivo.Capturada));
        filas.Single(f => f.Folio == "F-2").Estado.Should().Be(nameof(EstadoPasivo.Cancelada));
    }

    [Fact]
    public async Task Ids_vacios_devuelven_lista_vacia()
    {
        await using var db = NuevaDb();
        (await new CxpFacturasOcReadAdapter(db).ListarPorOcAsync([], CancellationToken.None)).Should().BeEmpty();
    }

    private static CuentasPorPagarDbContext NuevaDb()
    {
        var options = new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase($"cxp-facturas-oc-adapter-{Guid.NewGuid():N}")
            .Options;
        return new CuentasPorPagarDbContext(options, new BypassedEmpresaContext(), new P8.PeriodoAbiertoStub(), new P8.PeriodoAbiertoStub());
    }

    private static Domain.FacturaProveedor.FacturaProveedor CrearFactura(Guid ocId, string folio, decimal diferencia) =>
        Domain.FacturaProveedor.FacturaProveedor.CapturarConOc(
            empresaId: Guid.NewGuid(),
            cfdiRecibidoId: null,
            uuidCfdi: null,
            proveedorId: Guid.NewGuid(),
            sucursalId: Guid.NewGuid(),
            folioProveedor: folio, serieProveedor: "A",
            fechaDocumento: DateTimeOffset.UtcNow,
            fechaContabilizacion: DateTimeOffset.UtcNow,
            fechaVencimiento: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            moneda: "MXN", tipoCambio: null,
            subtotal: 1000m, descuentos: 0m,
            impuestosTrasladados: 160m, retenciones: 0m, total: 1160m,
            ordenCompraId: ocId,
            encargadoComprasSnapshot: null,
            tolerancia: Tolerancia.MontoAbsoluto(0.99m),
            diferenciaContraOc: diferencia,
            redondeoAplicado: 0m,
            ahora: DateTimeOffset.UtcNow);

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
