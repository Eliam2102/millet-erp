using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.Recepcion;

/// <summary>
/// Tests de integración para <see cref="ComprasOcReadAdapter"/> contra PostgreSQL (CA2.10).
/// Verifica que:
/// 1) Una OC autorizada mixta (1 física + 1 servicio) solo retorna la línea física en OcLectura.Lineas.
/// 2) Una OC autorizada 100% servicios retorna 0 líneas en OcLectura.Lineas (ninguna recibible en Almacén).
/// </summary>
public class ComprasOcReadAdapterIntegrationTests : IClassFixture<StubsWebApplicationFactory>
{
    private readonly StubsWebApplicationFactory _factory;

    public ComprasOcReadAdapterIntegrationTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static int _folioSeq = 910000;

    [Fact]
    public async Task ObtenerAsync_OcMixta_ExcluyeLineasDeServicio()
    {
        var articuloFisicoId = Guid.CreateVersion7();
        var articuloServicioId = Guid.CreateVersion7();
        var lineaFisicaId = Guid.CreateVersion7();
        var lineaServicioId = Guid.CreateVersion7();

        var folio = $"OC-MID2026-{Interlocked.Increment(ref _folioSeq):000000}";
        var oc = NewOcAutorizada(
            folio: folio,
            lineasConfig:
            [
                (lineaFisicaId, articuloFisicoId, 10m, "PZA", 100m, false),
                (lineaServicioId, articuloServicioId, 1m, "SER", 500m, true)
            ]);

        await WithDbAsync(async db =>
        {
            db.OrdenesCompra.Add(oc);
            await db.SaveChangesAsync();
        });

        await WithDbAsync(async db =>
        {
            var adapter = new ComprasOcReadAdapter(db);
            var lectura = await adapter.ObtenerAsync(oc.Id, CancellationToken.None);

            lectura.Should().NotBeNull();
            lectura!.Id.Should().Be(oc.Id);
            lectura.Estado.Should().Be("Autorizada");

            // CA2.10: la línea de servicio debe haber sido excluida
            lectura.Lineas.Should().HaveCount(1);
            lectura.Lineas.Single().LineaId.Should().Be(lineaFisicaId);
            lectura.Lineas.Single().ArticuloId.Should().Be(articuloFisicoId);
        });
    }

    [Fact]
    public async Task ObtenerAsync_OcSoloServicios_RetornaCeroLineasRecibibles()
    {
        var articuloServicioId = Guid.CreateVersion7();
        var lineaServicioId = Guid.CreateVersion7();

        var folio = $"OC-MID2026-{Interlocked.Increment(ref _folioSeq):000000}";
        var oc = NewOcAutorizada(
            folio: folio,
            lineasConfig:
            [
                (lineaServicioId, articuloServicioId, 2m, "SER", 250m, true)
            ]);

        await WithDbAsync(async db =>
        {
            db.OrdenesCompra.Add(oc);
            await db.SaveChangesAsync();
        });

        await WithDbAsync(async db =>
        {
            var adapter = new ComprasOcReadAdapter(db);
            var lectura = await adapter.ObtenerAsync(oc.Id, CancellationToken.None);

            lectura.Should().NotBeNull();
            lectura!.Id.Should().Be(oc.Id);

            // CA2.10: 100% servicios => 0 líneas en el adaptador para recepción
            lectura.Lineas.Should().BeEmpty();
        });
    }

    private static OrdenCompra NewOcAutorizada(
        string folio,
        (Guid lineaId, Guid articuloId, decimal cantidad, string uom, decimal precio, bool esServicio)[] lineasConfig)
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse(folio),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Test CA2.10");

        var deptoId = Guid.CreateVersion7();
        foreach (var l in lineasConfig)
        {
            oc.AgregarLineaManual(
                lineaId: l.lineaId,
                articuloId: l.articuloId,
                cantidad: l.cantidad,
                unidadMedida: l.uom,
                precioUnitario: l.precio,
                departamentoSolicitanteId: deptoId,
                esServicio: l.esServicio);
        }

        var ahora = DateTimeOffset.UtcNow;
        oc.EnviarAAutorizacion(ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel2, Guid.CreateVersion7(), ahora);

        return oc;
    }

    private async Task WithDbAsync(Func<ComprasDbContext, Task> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        await action(db);
    }
}
