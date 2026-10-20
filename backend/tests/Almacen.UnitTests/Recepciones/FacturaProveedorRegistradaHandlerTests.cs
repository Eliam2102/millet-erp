using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Almacen.Application.EventListeners;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// 86e3tfedr: con <c>RecepcionId</c> en <c>factura.registrada.v1</c> Almacén liga la factura
/// solo a esa recepción (principal + revisión); sin él conserva el comportamiento anterior.
/// </summary>
public sealed class FacturaProveedorRegistradaHandlerTests
{
    [Fact]
    public async Task Con_RecepcionId_liga_solo_la_elegida()
    {
        await using var db = await NuevaDbAsync();
        var ocId = Guid.NewGuid();
        var primera = AgregarRecepcionB(db, ocId, 1);
        var segunda = AgregarRecepcionB(db, ocId, 2);
        await db.SaveChangesAsync();

        var facturaId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await Handle(db, eventId, facturaId, ocId, primera);

        (await db.Movimientos.SingleAsync(m => m.Id == primera)).FacturaId.Should().Be(facturaId);
        (await db.Movimientos.SingleAsync(m => m.Id == segunda)).FacturaId.Should().BeNull();
        (await db.Set<EventoProcesado>().CountAsync(e => e.EventoId == eventId)).Should().Be(1);
    }

    [Fact]
    public async Task Sin_RecepcionId_liga_todas_las_pendientes()
    {
        await using var db = await NuevaDbAsync();
        var ocId = Guid.NewGuid();
        var primera = AgregarRecepcionB(db, ocId, 1);
        var segunda = AgregarRecepcionB(db, ocId, 2);
        await db.SaveChangesAsync();

        var facturaId = Guid.NewGuid();
        await Handle(db, Guid.NewGuid(), facturaId, ocId, null);

        (await db.Movimientos.SingleAsync(m => m.Id == primera)).FacturaId.Should().Be(facturaId);
        (await db.Movimientos.SingleAsync(m => m.Id == segunda)).FacturaId.Should().Be(facturaId);
    }

    [Fact]
    public async Task Liga_principal_y_su_movimiento_de_revision()
    {
        await using var db = await NuevaDbAsync();
        var ocId = Guid.NewGuid();
        var principal = AgregarRecepcionB(db, ocId, 1);
        var revision = AgregarRecepcionB(db, ocId, 2, recepcionOrigenId: principal);
        await db.SaveChangesAsync();

        var facturaId = Guid.NewGuid();
        await Handle(db, Guid.NewGuid(), facturaId, ocId, principal);

        (await db.Movimientos.SingleAsync(m => m.Id == principal)).FacturaId.Should().Be(facturaId);
        (await db.Movimientos.SingleAsync(m => m.Id == revision)).FacturaId.Should().Be(facturaId);
    }

    [Fact]
    public async Task RecepcionId_de_otra_OC_no_liga_nada_ni_registra_evento()
    {
        await using var db = await NuevaDbAsync();
        var ocId = Guid.NewGuid();
        var propia = AgregarRecepcionB(db, ocId, 1);
        var ajena = AgregarRecepcionB(db, Guid.NewGuid(), 2);
        await db.SaveChangesAsync();

        var eventId = Guid.NewGuid();
        await Handle(db, eventId, Guid.NewGuid(), ocId, ajena);

        (await db.Movimientos.SingleAsync(m => m.Id == propia)).FacturaId.Should().BeNull();
        (await db.Movimientos.SingleAsync(m => m.Id == ajena)).FacturaId.Should().BeNull();
        (await db.Set<EventoProcesado>().AnyAsync(e => e.EventoId == eventId)).Should().BeFalse();
    }

    private static Task Handle(
        AlmacenDbContext db, Guid eventId, Guid facturaId, Guid ocId, Guid? recepcionId) =>
        new FacturaProveedorRegistradaHandler(db, NullLogger<FacturaProveedorRegistradaHandler>.Instance)
            .Handle(
                new FacturaProveedorRegistradaCommand(
                    eventId,
                    new FacturaProveedorRegistradaPayload(
                        Guid.NewGuid(), DateTimeOffset.UtcNow, facturaId, ocId, 100m, [],
                        RecepcionId: recepcionId)),
                CancellationToken.None);

    private static Guid AgregarRecepcionB(
        AlmacenDbContext db, Guid ocId, int folio, Guid? recepcionOrigenId = null)
    {
        var mov = new MovimientoInventario(
            id: Guid.NewGuid(),
            tipo: TipoMovimiento.EntradaCompra,
            empresaId: Guid.NewGuid(),
            fechaMovimiento: new DateOnly(2026, 7, 14));
        mov.AgregarLinea(new LineaMovimiento(
            Guid.NewGuid(), mov.Id, 1, Guid.NewGuid(), 1m, "PZA", 10m));

        // Métodos internal — reflexión, mismo patrón que CfdiRecibidoIngresadoHandlerTests.
        typeof(MovimientoInventario)
            .GetMethod("VincularRecepcionVarianteB",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(mov, new object?[] { ocId, null, "blob/pl.pdf" });
        if (recepcionOrigenId is Guid origen)
            typeof(MovimientoInventario)
                .GetMethod("VincularRecepcionPrincipal",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(mov, new object?[] { origen });

        mov.Registrar(
            FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2026, folio),
            registradoPor: Guid.NewGuid());

        db.Movimientos.Add(mov);
        return mov.Id;
    }

    private static async Task<AlmacenDbContext> NuevaDbAsync()
    {
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-factura-reg-{Guid.NewGuid():N}")
            .Options;
        var db = new AlmacenDbContext(opts, new BypassedEmpresaContext());
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
