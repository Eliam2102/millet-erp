using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Application.Oc.Autorizar;
using Millet.Compras.Application.Oc.Cancelar;
using Millet.Compras.Application.Oc.Eventos;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Pdf;
using Millet.Compras.Domain.Ports.Blob;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.Outbox;

/// <summary>Regresiones con PostgreSQL, MediatR, mappers e interceptor reales.</summary>
public class OperacionesComprasOutboxTests : IClassFixture<StubsWebApplicationFactory>
{
    private readonly StubsWebApplicationFactory _factory;
    private static readonly UsuarioPrueba Usuario = new();

    public OperacionesComprasOutboxTests(StubsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task AutorizarOc_SinListenerPdf_PersisteExactamenteUnEvento()
    {
        // En el código anterior falla de forma determinista: ningún listener
        // puede hacer un SaveChanges accidental después de encolar el evento.
        await using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services.Where(s => s.ImplementationType == typeof(OrdenCompraAutorizadaPdfListener)).ToArray())
                services.Remove(descriptor);
        }));
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = sp.GetRequiredService<ComprasDbContext>();
            var oc = await CrearOcAsync(db);
            id = oc.Id;
            await AutorizarAsync(sp, id);
        } // Desechar el scope sin ningún SaveChanges extra.

        await VerificarAsync(factory.Services, id, "autorizada", EstadoOrdenCompra.Autorizada);
    }

    [Fact]
    public async Task CancelarOc_PersisteExactamenteUnEvento()
    {
        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = sp.GetRequiredService<ComprasDbContext>();
            var oc = await CrearOcAsync(db, autorizada: true);
            id = oc.Id;
            // Motivo del seed: crear uno propio cambiaría el catálogo que
            // comparten las demás suites (AutorizacionesEndpointsTests espera 7).
            var motivo = await db.MotivosRechazo.AsNoTracking()
                .FirstAsync(m => m.Clave == "RECH-DUP");

            await new CancelarOrdenCompraHandler(db, Usuario, sp.GetRequiredService<IClock>(),
                sp.GetRequiredService<IPublisher>()).Handle(new(id, motivo.Id, null), CancellationToken.None);
        }
        await VerificarAsync(_factory.Services, id, "cancelada", EstadoOrdenCompra.Cancelada);
    }

    [Fact]
    public async Task AutorizarOc_PdfFalla_EstadoYEventoYaEstanPersistidos()
    {
        await using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGenerarPdfOrdenCompraPort>();
            services.AddSingleton<IGenerarPdfOrdenCompraPort, PdfQueFalla>();
        }));
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var oc = await CrearOcAsync(sp.GetRequiredService<ComprasDbContext>());
            id = oc.Id;
            await Assert.ThrowsAsync<InvalidOperationException>(() => AutorizarAsync(sp, id));
        }
        await VerificarAsync(factory.Services, id, "autorizada", EstadoOrdenCompra.Autorizada);
    }

    [Fact]
    public async Task AutorizarOc_PdfLeeEstadoYOutboxPersistidos_YNoDuplicaEvento()
    {
        await using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGenerarPdfOrdenCompraPort>();
            services.AddSingleton<IGenerarPdfOrdenCompraPort, PdfQueVerificaPersistencia>();
            services.RemoveAll<IAlmacenarBlobPort>();
            services.AddSingleton<IAlmacenarBlobPort, BlobEnMemoria>();
        }));
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var oc = await CrearOcAsync(sp.GetRequiredService<ComprasDbContext>());
            id = oc.Id;
            await AutorizarAsync(sp, id);
        }
        await VerificarAsync(factory.Services, id, "autorizada", EstadoOrdenCompra.Autorizada);
        using var verifyScope = factory.Services.CreateScope();
        using var verifyBypass = verifyScope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = verifyScope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        Assert.Single(await db.OrdenCompraPdfs.Where(p => p.OrdenCompraId == id).ToListAsync());
    }

    [Fact]
    public async Task AutorizarOc_Rollback_NoPersisteEstadoNiEvento()
    {
        await using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services.Where(s => s.ImplementationType == typeof(OrdenCompraAutorizadaPdfListener)).ToArray())
                services.Remove(descriptor);
        }));
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = sp.GetRequiredService<ComprasDbContext>();
            var oc = await CrearOcAsync(db);
            id = oc.Id;
            await using var tx = await db.Database.BeginTransactionAsync();
            await AutorizarAsync(sp, id);
            Assert.Single(await db.OutboxEntries.Where(e => e.IntegrationEmpresaId == oc.EmpresaId).ToListAsync());
            await tx.RollbackAsync();
        }
        using var verifyScope = factory.Services.CreateScope();
        using var verifyBypass = verifyScope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var persisted = await verifyDb.OrdenesCompra.SingleAsync(o => o.Id == id);
        Assert.Equal(EstadoOrdenCompra.EnAutorizacionDireccion, persisted.Estado);
        Assert.Empty(await verifyDb.OutboxEntries.Where(e => e.IntegrationEmpresaId == persisted.EmpresaId).ToListAsync());
    }

    private static Task AutorizarAsync(IServiceProvider sp, Guid id) =>
        new AutorizarOrdenCompraHandler(sp.GetRequiredService<ComprasDbContext>(),
            sp.GetRequiredService<CompartidoDbContext>(), Usuario, sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IPublisher>()).Handle(new(id, NivelAutorizacion.Nivel2, null), CancellationToken.None);

    private static async Task<OrdenCompra> CrearOcAsync(ComprasDbContext db, bool autorizada = false)
    {
        // Empresa aislada por prueba y referencias lógicas; proveedor activo del seed.
        var oc = new OrdenCompra(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000001"), 2026,
            Guid.Parse("00000005-0001-0000-0000-000000000001"), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Usuario.UserId!.Value, Usuario.UserId.Value,
            new DateOnly(2026, 10, 7), sinRequisicionPrevia: true, motivoSinRequisicion: "Prueba ficticia outbox");
        oc.AgregarLineaManual(Guid.CreateVersion7(), Guid.CreateVersion7(), 1m, "PZA", 100m, Guid.CreateVersion7());
        var ahora = DateTimeOffset.UtcNow;
        oc.EnviarAAutorizacion(ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Usuario.UserId.Value, ahora);
        if (autorizada)
            oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel2, Usuario.UserId.Value, ahora);
        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return oc;
    }

    private static async Task VerificarAsync(IServiceProvider services, Guid id, string evento, EstadoOrdenCompra estado)
    {
        using var scope = services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var oc = await db.OrdenesCompra.AsNoTracking().SingleAsync(o => o.Id == id);
        Assert.Equal(estado, oc.Estado);
        var fila = Assert.Single(await db.OutboxEntries.AsNoTracking()
            .Where(e => e.IntegrationEmpresaId == oc.EmpresaId && e.EventType == $"compras.orden-compra.{evento}.v1")
            .ToListAsync());
        using var payload = JsonDocument.Parse(fila.Payload);
        Assert.Equal(id, payload.RootElement.GetProperty("OrdenCompraId").GetGuid());
        Assert.Null(fila.PublishedAt);
    }

    private sealed class UsuarioPrueba : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.CreateVersion7();
        public string? UserName => "Prueba outbox";
    }

    private sealed class PdfQueVerificaPersistencia(IServiceProvider services) : IGenerarPdfOrdenCompraPort
    {
        public async Task<PdfOrdenCompraGenerado> GenerarAsync(OrdenCompra oc, CancellationToken cancellationToken)
        {
            // Otra conexión: el PDF solo se ejecuta cuando estado y outbox ya son visibles.
            await VerificarAsync(services, oc.Id, "autorizada", EstadoOrdenCompra.Autorizada);
            return new PdfOrdenCompraGenerado([1, 2, 3], "application/pdf", "prueba.pdf");
        }
    }

    private sealed class BlobEnMemoria : IAlmacenarBlobPort
    {
        public Task<string> SubirAsync(Guid blobId, Stream contenido, string contentType,
            string nombreArchivoOriginal, CancellationToken cancellationToken) => Task.FromResult($"memoria/{blobId}");
        public Task<Stream> ObtenerStreamAsync(string blobUrl, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public Task EliminarAsync(string blobUrl, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class PdfQueFalla : IGenerarPdfOrdenCompraPort
    {
        public Task<PdfOrdenCompraGenerado> GenerarAsync(OrdenCompra oc, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Fallo de PDF simulado");
    }
}
