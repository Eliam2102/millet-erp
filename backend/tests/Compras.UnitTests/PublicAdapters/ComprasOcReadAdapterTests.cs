using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;

namespace Millet.Compras.UnitTests.PublicAdapters;

/// <summary>
/// Tests del <see cref="ComprasOcReadAdapter"/> — en particular que la lectura
/// de folio para presentación (<c>ObtenerFoliosAsync</c>) es state-agnostic:
/// resuelve el folio aunque la OC ya haya avanzado a un estado que
/// <c>ObtenerAsync</c> (lectura operativa, gateada por <c>Autorizada</c>)
/// rechaza. ADR-0042. Cubre el caso de una recepción histórica cuya OC ya
/// está Cancelada.
/// </summary>
public class ComprasOcReadAdapterTests
{
    [Fact]
    public async Task ObtenerFolios_resuelve_aunque_la_OC_este_cancelada()
    {
        await using var db = await NuevaDbAsync();
        var oc = CrearOcCancelada(folio: "OC-MID2026-000050");
        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();

        var adapter = new ComprasOcReadAdapter(db);

        // Lectura de presentación: state-agnostic. La OC está Cancelada —un
        // estado que la lectura operativa ObtenerAsync rechaza por su filtro
        // (solo Autorizada)— y aun así el folio resuelve.
        //
        // No ejercitamos ObtenerAsync aquí: su Include(Lineas) materializa el
        // VO Money, que el provider EF InMemory no sabe shapear (limitación del
        // provider de test, no del código; en PostgreSQL funciona). Su filtro de
        // estado está cubierto por los tests del flujo de recepción.
        var folios = await adapter.ObtenerFoliosAsync(new[] { oc.Id }, CancellationToken.None);

        folios.Should().ContainKey(oc.Id);
        folios[oc.Id].Should().Be("OC-MID2026-000050");
    }

    [Fact]
    public async Task ObtenerFolios_ignora_ids_inexistentes()
    {
        await using var db = await NuevaDbAsync();
        var adapter = new ComprasOcReadAdapter(db);

        var folios = await adapter.ObtenerFoliosAsync(new[] { Guid.NewGuid() }, CancellationToken.None);

        folios.Should().BeEmpty();
    }

    [Fact]
    public async Task ObtenerFolios_lista_vacia_no_consulta()
    {
        await using var db = await NuevaDbAsync();
        var adapter = new ComprasOcReadAdapter(db);

        var folios = await adapter.ObtenerFoliosAsync(Array.Empty<Guid>(), CancellationToken.None);

        folios.Should().BeEmpty();
    }

    private static OrdenCompra CrearOcCancelada(string folio)
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Folio.Parse(folio),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Test");

        // Cancelar desde Borrador (no terminal, SinRecepcion por default).
        oc.Cancelar(
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoCancelacionId: Guid.CreateVersion7(),
            motivoCancelacionTexto: "Cancelada para test state-agnostic");

        oc.Estado.Should().Be(EstadoOrdenCompra.Cancelada);
        return oc;
    }

    private static async Task<ComprasDbContext> NuevaDbAsync()
    {
        var opts = new DbContextOptionsBuilder<ComprasDbContext>()
            .UseInMemoryDatabase($"compras-oc-read-adapter-{Guid.NewGuid():N}")
            .Options;
        var db = new ComprasDbContext(opts, new BypassedEmpresaContext());
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
