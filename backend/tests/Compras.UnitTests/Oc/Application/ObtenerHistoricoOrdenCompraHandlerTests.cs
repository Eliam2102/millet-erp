using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Oc.ObtenerHistorico;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.Compras.UnitTests.Oc.Application;

/// <summary>El historial de la OC debe mostrar el resumen legible, no el JSON de cambios.</summary>
public class ObtenerHistoricoOrdenCompraHandlerTests
{
    [Fact]
    public async Task Prefiere_el_resumen_legible_sobre_el_JSON_de_cambios()
    {
        var ocId = Guid.NewGuid();
        await using var db = await NuevaDbAsync();
        db.Set<AuditLogEntry>().AddRange(
            Evento(ocId, resumen: "Creó OrdenCompra OC-MID2026-000001", cambios: "{\"snapshot\": {\"Id\": \"x\"}}"),
            Evento(ocId, resumen: "", cambios: "{\"snapshot\": {\"Id\": \"y\"}}"));
        await db.SaveChangesAsync();

        var r = await new ObtenerHistoricoOrdenCompraHandler(db)
            .Handle(new ObtenerHistoricoOrdenCompraQuery(ocId), CancellationToken.None);

        r.Eventos.Select(e => e.Resumen).Should().Equal(
            "Creó OrdenCompra OC-MID2026-000001",
            "{\"snapshot\": {\"Id\": \"y\"}}"); // sin resumen: cae al JSON
    }

    private static AuditLogEntry Evento(Guid ocId, string resumen, string cambios) => new()
    {
        Id = Guid.NewGuid(), Timestamp = DateTimeOffset.UtcNow, Modulo = "Compras", Entidad = "OrdenCompra",
        Operacion = "crear", AggregateRootId = ocId, Cambios = cambios, Resumen = resumen,
    };

    private static async Task<ComprasDbContext> NuevaDbAsync()
    {
        var opts = new DbContextOptionsBuilder<ComprasDbContext>()
            .UseInMemoryDatabase($"compras-historico-oc-{Guid.NewGuid():N}").Options;
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
