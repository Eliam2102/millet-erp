using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.DevolucionesProveedor;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.DevolucionesProveedor;

/// <summary>
/// Tests del enriquecimiento ADR-0042 en las queries de devoluciones a
/// proveedor: la bandeja resuelve la razón social en <b>batch</b> sobre los
/// ProveedorId distintos de la página (una sola llamada al puerto), el
/// detalle resuelve proveedor (single) + artículos de las líneas (batch), y
/// todo cae a <c>null</c> cuando el puerto no resuelve (el frontend cae al
/// id truncado).
/// </summary>
public class DevolucionesProveedorQueriesTests
{
    private static readonly Guid Prov1 = Guid.NewGuid();
    private static readonly Guid Prov2 = Guid.NewGuid();
    private static readonly Guid Art1 = Guid.NewGuid();
    private static readonly Guid Art2 = Guid.NewGuid();

    [Fact]
    public async Task Bandeja_resuelve_razon_social_en_batch_y_cae_a_null_sin_resolver()
    {
        await using var db = await NuevaDbAsync();
        // 2 devoluciones de Prov1 (→ un solo id distinto en el batch),
        // 1 de Prov2 que el puerto NO resuelve.
        AgregarDevolucion(db, Prov1);
        AgregarDevolucion(db, Prov1);
        AgregarDevolucion(db, Prov2);
        await db.SaveChangesAsync();

        var proveedores = new FakeProveedorReadPort
        {
            Proveedores = new() { [Prov1] = Lectura(Prov1, "PROV.0001", "Vidrios del Norte SA") },
        };
        var handler = new ListarDevolucionesProveedorHandler(db, proveedores);

        var page = await handler.Handle(
            new ListarDevolucionesProveedorQuery(null, null, null, Offset: 0, Limit: 50),
            CancellationToken.None);

        page.Items.Should().HaveCount(3);
        page.Items.Where(i => i.ProveedorId == Prov1)
            .Should().OnlyContain(i => i.ProveedorNombre == "Vidrios del Norte SA");
        page.Items.Single(i => i.ProveedorId == Prov2).ProveedorNombre.Should().BeNull();

        // Batch real: UNA llamada con los ProveedorId DISTINTOS de la página.
        proveedores.LlamadasBatch.Should().Be(1);
        proveedores.UltimosIds.Should().BeEquivalentTo(new[] { Prov1, Prov2 });
    }

    [Fact]
    public async Task Detalle_resuelve_proveedor_y_articulos_de_lineas_en_batch()
    {
        await using var db = await NuevaDbAsync();
        // 2 líneas del mismo artículo + 1 de otro que NO resuelve.
        var dev = AgregarDevolucion(db, Prov1, lineas: new[] { Art1, Art1, Art2 });
        await db.SaveChangesAsync();

        var proveedores = new FakeProveedorReadPort
        {
            Proveedores = new() { [Prov1] = Lectura(Prov1, "PROV.0001", "Vidrios del Norte SA") },
        };
        var articulos = new FakeArticuloReadPort
        {
            Articulos = new()
            {
                [Art1] = new ArticuloLectura(Art1, "IPP60001", "Aceite de corte", "PZA", null, null, true),
            },
        };
        var handler = new ObtenerDevolucionProveedorPorIdHandler(
            db, proveedores, articulos, new FakeCxpDocumentosReadPort());

        var detalle = await handler.Handle(
            new ObtenerDevolucionProveedorPorIdQuery(dev.Id), CancellationToken.None);

        detalle.Should().NotBeNull();
        detalle!.ProveedorNombre.Should().Be("Vidrios del Norte SA");

        detalle.Lineas.Where(l => l.ArticuloId == Art1)
            .Should().OnlyContain(l =>
                l.ArticuloClave == "IPP60001" && l.ArticuloDescripcion == "Aceite de corte");
        var sinResolver = detalle.Lineas.Single(l => l.ArticuloId == Art2);
        sinResolver.ArticuloClave.Should().BeNull();
        sinResolver.ArticuloDescripcion.Should().BeNull();

        // Artículos en batch: UNA llamada con los ids DISTINTOS (Art1 una vez).
        articulos.LlamadasBatch.Should().Be(1);
        articulos.UltimosIds.Should().BeEquivalentTo(new[] { Art1, Art2 });
    }

    [Fact]
    public async Task Detalle_cae_a_null_cuando_el_proveedor_no_resuelve()
    {
        await using var db = await NuevaDbAsync();
        var dev = AgregarDevolucion(db, Prov1, lineas: new[] { Art1 });
        await db.SaveChangesAsync();

        var handler = new ObtenerDevolucionProveedorPorIdHandler(
            db, new FakeProveedorReadPort(), new FakeArticuloReadPort(),
            new FakeCxpDocumentosReadPort()); // puertos vacíos

        var detalle = await handler.Handle(
            new ObtenerDevolucionProveedorPorIdQuery(dev.Id), CancellationToken.None);

        detalle.Should().NotBeNull();
        detalle!.ProveedorNombre.Should().BeNull();
        detalle.Lineas.Single().ArticuloClave.Should().BeNull();
        detalle.NotaCreditoFolio.Should().BeNull();
    }

    [Fact]
    public async Task Detalle_resuelve_folio_de_NC_fiscal_via_puerto_CxP()
    {
        await using var db = await NuevaDbAsync();
        var ncId = Guid.NewGuid();
        // Ciclo completo hasta ConciliadaConNcFiscal para que la devolución
        // tenga NotaCreditoFiscalId poblado.
        var dev = AgregarDevolucion(db, Prov1, lineas: new[] { Art1 });
        dev.AgregarEvidencia(new EvidenciaDevolucionProveedor(
            Guid.NewGuid(), dev.Id, "foto", "evidencia.jpg", "blob://x", null));
        dev.SolicitarAutorizacion();
        dev.Autorizar(Guid.NewGuid());
        dev.MarcarRegistrada(Guid.NewGuid(), "M-SAL2026-000001");
        dev.ConciliarConNcFiscal(ncId);
        await db.SaveChangesAsync();

        var cxp = new FakeCxpDocumentosReadPort
        {
            FoliosNc = new() { [ncId] = "NC-B-77" },
        };
        var handler = new ObtenerDevolucionProveedorPorIdHandler(
            db, new FakeProveedorReadPort(), new FakeArticuloReadPort(), cxp);

        var detalle = await handler.Handle(
            new ObtenerDevolucionProveedorPorIdQuery(dev.Id), CancellationToken.None);

        detalle.Should().NotBeNull();
        detalle!.NotaCreditoFolio.Should().Be("NC-B-77");
    }

    // ─── Infra de test ───

    private static ProveedorLectura Lectura(Guid id, string clave, string razonSocial) =>
        new(id, clave, razonSocial, Rfc: null, EsActivo: true);

    private static DevolucionAProveedor AgregarDevolucion(
        AlmacenDbContext db, Guid proveedorId, Guid[]? lineas = null)
    {
        var dev = new DevolucionAProveedor(
            id: Guid.NewGuid(),
            empresaId: Guid.NewGuid(),
            proveedorId: proveedorId,
            motivo: "Material dañado en tránsito",
            solicitadaPor: Guid.NewGuid());

        var posicion = 1;
        foreach (var articuloId in lineas ?? new[] { Guid.NewGuid() })
        {
            dev.AgregarLinea(new LineaDevolucionProveedor(
                Guid.NewGuid(), dev.Id, posicion++, articuloId, 1m, "PZA", 10m));
        }

        db.Set<DevolucionAProveedor>().Add(dev);
        return dev;
    }

    private static async Task<AlmacenDbContext> NuevaDbAsync()
    {
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-devoluciones-queries-{Guid.NewGuid():N}")
            .Options;
        var db = new AlmacenDbContext(opts, new BypassedEmpresaContext());
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class FakeProveedorReadPort : IProveedorReadPort
    {
        public Dictionary<Guid, ProveedorLectura> Proveedores { get; set; } = new();
        public int LlamadasBatch { get; private set; }
        public Guid[] UltimosIds { get; private set; } = Array.Empty<Guid>();

        public Task<ProveedorLectura?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken) =>
            Task.FromResult(Proveedores.TryGetValue(proveedorId, out var p) ? p : null);

        public Task<IReadOnlyDictionary<Guid, ProveedorLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> proveedorIds, CancellationToken cancellationToken)
        {
            LlamadasBatch++;
            UltimosIds = proveedorIds.ToArray();
            return Task.FromResult<IReadOnlyDictionary<Guid, ProveedorLectura>>(
                Proveedores.Where(kv => proveedorIds.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value));
        }
    }

    private sealed class FakeArticuloReadPort : IArticuloReadPort
    {
        public Dictionary<Guid, ArticuloLectura> Articulos { get; set; } = new();
        public int LlamadasBatch { get; private set; }
        public Guid[] UltimosIds { get; private set; } = Array.Empty<Guid>();

        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken cancellationToken) =>
            Task.FromResult(Articulos.TryGetValue(articuloId, out var a) ? a : null);

        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> articuloIds, CancellationToken cancellationToken)
        {
            LlamadasBatch++;
            UltimosIds = articuloIds.ToArray();
            return Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(
                Articulos.Where(kv => articuloIds.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value));
        }
    }

    private sealed class FakeCxpDocumentosReadPort : ICxpDocumentosReadPort
    {
        public Dictionary<Guid, string> FoliosFactura { get; set; } = new();
        public Dictionary<Guid, string> UuidsCfdi { get; set; } = new();
        public Dictionary<Guid, string> FoliosNc { get; set; } = new();

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosFacturaAsync(
            IReadOnlyCollection<Guid> facturaIds, CancellationToken cancellationToken) =>
            Filtrar(FoliosFactura, facturaIds);

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerUuidsFiscalesCfdiAsync(
            IReadOnlyCollection<Guid> cfdiRecibidoIds, CancellationToken cancellationToken) =>
            Filtrar(UuidsCfdi, cfdiRecibidoIds);

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosNotaCreditoAsync(
            IReadOnlyCollection<Guid> notaCreditoIds, CancellationToken cancellationToken) =>
            Filtrar(FoliosNc, notaCreditoIds);

        private static Task<IReadOnlyDictionary<Guid, string>> Filtrar(
            Dictionary<Guid, string> fuente, IReadOnlyCollection<Guid> ids) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                fuente.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
