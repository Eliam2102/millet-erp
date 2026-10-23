using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Application.EventListeners.Tesoreria;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Revision;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.UnitTests.P8;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

public sealed class DetencionPorDanoServiceTests : IDisposable
{
    private static readonly DateTimeOffset Ahora = DateTimeOffset.Parse("2026-10-16T21:00:00Z");

    private readonly CuentasPorPagarDbContext _db = new(
        new DbContextOptionsBuilder<CuentasPorPagarDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new SinEmpresa(), new PeriodoAbiertoStub(), new PeriodoAbiertoStub());

    public DetencionPorDanoServiceTests() => _db.Database.EnsureCreated();

    public void Dispose() => _db.Dispose();

    private DetencionPorDanoService Servicio(bool conCompras = true) =>
        new(_db, new AlmacenRevisionFake(), new DependenciasFake(conCompras), NullLogger<DetencionPorDanoService>.Instance);

    private static Domain.FacturaProveedor.FacturaProveedor Factura(Guid lineaOcId)
    {
        var f = Domain.FacturaProveedor.FacturaProveedor.CapturarConOc(
            empresaId: Guid.NewGuid(), cfdiRecibidoId: null, uuidCfdi: null,
            proveedorId: Guid.NewGuid(), sucursalId: Guid.NewGuid(),
            folioProveedor: "F-001", serieProveedor: "A",
            fechaDocumento: Ahora, fechaContabilizacion: Ahora,
            fechaVencimiento: new DateOnly(2026, 11, 30), moneda: "MXN", tipoCambio: null,
            subtotal: 1000m, descuentos: 0m, impuestosTrasladados: 160m, retenciones: 0m, total: 1160m,
            ordenCompraId: Guid.NewGuid(), encargadoComprasSnapshot: null,
            tolerancia: Tolerancia.MontoAbsoluto(0.99m),
            diferenciaContraOc: 0m, redondeoAplicado: 0m, ahora: Ahora);
        f.AgregarLinea(null, null, "Vidrio", 1m, "H87", null, 1000m, 1000m, null, lineaOcId, null);
        return f;
    }

    private static HashSet<Guid> Dano(Guid lineaOcId) => [lineaOcId];

    [Fact]
    public async Task Linea_danada_de_la_misma_linea_de_OC_detiene_con_Danos_y_area_Compras()
    {
        var a = Guid.NewGuid();
        var f = Factura(a);

        (await Servicio().DetenerSiAplicaAsync(f, Dano(a), Ahora, default)).Should().BeTrue();

        f.Estado.Should().Be(EstadoPasivo.EnRevision);
        f.MotivoRevisionId.Should().Be(DetencionPorDanoService.MotivoDanosId);
        f.DependenciaRevisoraId.Should().Be(DependenciasFake.ComprasId);
    }

    [Fact]
    public async Task Factura_de_otra_linea_de_OC_no_se_detiene()
    {
        var f = Factura(Guid.NewGuid());

        (await Servicio().DetenerSiAplicaAsync(f, Dano(Guid.NewGuid()), Ahora, default)).Should().BeFalse();

        f.Estado.Should().Be(EstadoPasivo.Capturada);
    }

    [Fact]
    public async Task Factura_ya_en_revision_conserva_su_motivo()
    {
        var a = Guid.NewGuid();
        var f = Factura(a);
        var otro = Guid.NewGuid();
        f.EnviarARevision(otro, Guid.NewGuid(), null, Ahora);

        (await Servicio().DetenerSiAplicaAsync(f, Dano(a), Ahora, default)).Should().BeFalse();

        f.MotivoRevisionId.Should().Be(otro);
    }

    [Fact]
    public async Task Factura_cancelada_no_se_detiene()
    {
        var a = Guid.NewGuid();
        var f = Factura(a);
        f.Cancelar(MotivoCancelacion.ErrorCaptura, null, null, Ahora);

        (await Servicio().DetenerSiAplicaAsync(f, Dano(a), Ahora, default)).Should().BeFalse();

        f.Estado.Should().Be(EstadoPasivo.Cancelada);
    }

    [Fact]
    public async Task Subalmacen_fuera_de_revision_no_marca_dano()
    {
        var lineas = new[]
        {
            new LineaRecepcionPayload(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "PZA", 1m, 1m, 1m, Guid.NewGuid()),
        };

        (await Servicio().LineasOcConDanoAsync(lineas, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_areas_en_el_catalogo_no_detiene_ni_inventa_un_id()
    {
        var a = Guid.NewGuid();
        var f = Factura(a);

        (await Servicio(conCompras: false).DetenerSiAplicaAsync(f, Dano(a), Ahora, default)).Should().BeFalse();

        f.EnRevision.Should().BeFalse();
        f.DependenciaRevisoraId.Should().BeNull();
    }

    private sealed class SinEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
