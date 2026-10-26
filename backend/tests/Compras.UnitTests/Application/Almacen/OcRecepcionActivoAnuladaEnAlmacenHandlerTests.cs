using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Compras.Application.Almacen;
using Millet.Compras.Domain.Idempotencia;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.UnitTests.Application.Almacen;

/// <summary>
/// Anulación de una recepción de activos. El InMemory provider no materializa las líneas de OC
/// (owned types), así que aquí se cubre dedupe y contrato; el efecto sobre
/// <c>CantidadRecibida</c> usa <c>RegistrarRecepcionLinea</c>, ya cubierto en dominio.
/// </summary>
public sealed class OcRecepcionActivoAnuladaEnAlmacenHandlerTests
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Evento_ya_procesado_no_se_reprocesa()
    {
        await using var db = await CreateDbAsync();
        var eventoId = Guid.NewGuid();
        db.EventosProcesados.Add(new EventoProcesado(eventoId, OcRecepcionActivoAnuladaEnAlmacenHandler.EventType, "ya estaba"));
        await db.SaveChangesAsync();

        // Con la OC inexistente fallaría si no hiciera skip por dedupe.
        await NuevoHandler(db).Handle(new OcRecepcionActivoAnuladaEnAlmacenCommand(eventoId, Payload()), CancellationToken.None);

        (await db.EventosProcesados.CountAsync()).Should().Be(1);
    }

    [Fact]
    public void Deserializa_el_evento_que_publica_Almacen()
    {
        var body = """{"EmpresaId":"00000000-0000-0000-0000-000000000001","OcurridoEn":"2026-10-22T20:00:00Z","RecepcionActivoId":"00000000-0000-0000-0000-000000000002","RecepcionId":"00000000-0000-0000-0000-000000000003","FolioRecepcion":"M-ENT2026-000001","OrdenCompraId":"00000000-0000-0000-0000-000000000004","LineaOcId":"00000000-0000-0000-0000-000000000005","Cantidad":1,"CantidadRecibidaAcumulada":2,"Motivo":"Error","EventType":"almacen.recepcion_activo.anulada.v1"}""";
        var p = System.Text.Json.JsonSerializer.Deserialize<RecepcionActivoAnuladaAlmacenPayload>(body, JsonOpts);
        p!.CantidadRecibidaAcumulada.Should().Be(2m);
        p.LineaOcId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000005"));
    }

    private static OcRecepcionActivoAnuladaEnAlmacenHandler NuevoHandler(ComprasDbContext db)
        => new(db, new NullPublisher(), NullLogger<OcRecepcionActivoAnuladaEnAlmacenHandler>.Instance);

    private static RecepcionActivoAnuladaAlmacenPayload Payload()
        => new(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, 2m, "Error de captura");

    private static async Task<ComprasDbContext> CreateDbAsync()
    {
        var db = new ComprasDbContext(new DbContextOptionsBuilder<ComprasDbContext>().UseInMemoryDatabase($"compras-anula-activo-{Guid.NewGuid():N}").Options, new BypassedEmpresaContext());
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class NullPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
