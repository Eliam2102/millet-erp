using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Almacen.Application.EventListeners;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// Tests del enlace diferido de recepción variante A: el evento
/// <c>cuentas_por_pagar.cfdi.ingresado.v1</c> backfillea el
/// <c>CfdiRecibidoId</c> de recepciones registradas con folio fiscal
/// capturado a mano.
/// </summary>
public class CfdiRecibidoIngresadoHandlerTests
{
    private const string Uuid = "AD662D33-6934-459C-A128-BDF0393E0062";

    [Fact]
    public async Task Enlaza_recepcion_pendiente_por_uuid_y_registra_evento()
    {
        await using var db = await NuevaDbAsync();
        var movId = AgregarRecepcionRegistrada(db, uuidFiscal: Uuid, cfdiRecibidoId: null, folio: 1);
        await db.SaveChangesAsync();

        var cfdiId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await NuevoHandler(db).Handle(
            new CfdiRecibidoIngresadoCommand(eventId, NuevoPayload(cfdiId, Uuid)),
            CancellationToken.None);

        var mov = await db.Movimientos.SingleAsync(m => m.Id == movId);
        mov.CfdiRecibidoId.Should().Be(cfdiId);
        (await db.Set<EventoProcesado>().AnyAsync(e => e.EventoId == eventId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Uuid_en_minusculas_del_payload_tambien_matchea()
    {
        await using var db = await NuevaDbAsync();
        var movId = AgregarRecepcionRegistrada(db, uuidFiscal: Uuid, cfdiRecibidoId: null, folio: 2);
        await db.SaveChangesAsync();

        var cfdiId = Guid.NewGuid();
        await NuevoHandler(db).Handle(
            new CfdiRecibidoIngresadoCommand(
                Guid.NewGuid(), NuevoPayload(cfdiId, Uuid.ToLowerInvariant())),
            CancellationToken.None);

        var mov = await db.Movimientos.SingleAsync(m => m.Id == movId);
        mov.CfdiRecibidoId.Should().Be(cfdiId);
    }

    [Fact]
    public async Task Sin_recepciones_pendientes_no_hace_nada()
    {
        await using var db = await NuevaDbAsync();
        var eventId = Guid.NewGuid();

        await NuevoHandler(db).Handle(
            new CfdiRecibidoIngresadoCommand(eventId, NuevoPayload(Guid.NewGuid(), Uuid)),
            CancellationToken.None);

        (await db.Set<EventoProcesado>().AnyAsync(e => e.EventoId == eventId))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Recepcion_ya_enlazada_no_se_toca()
    {
        await using var db = await NuevaDbAsync();
        var cfdiOriginal = Guid.NewGuid();
        var movId = AgregarRecepcionRegistrada(db, uuidFiscal: Uuid, cfdiRecibidoId: cfdiOriginal, folio: 3);
        await db.SaveChangesAsync();

        await NuevoHandler(db).Handle(
            new CfdiRecibidoIngresadoCommand(
                Guid.NewGuid(), NuevoPayload(Guid.NewGuid(), Uuid)),
            CancellationToken.None);

        var mov = await db.Movimientos.SingleAsync(m => m.Id == movId);
        mov.CfdiRecibidoId.Should().Be(cfdiOriginal);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static CfdiRecibidoIngresadoHandler NuevoHandler(AlmacenDbContext db) =>
        new(db, NullLogger<CfdiRecibidoIngresadoHandler>.Instance);

    private static CfdiRecibidoIngresadoPayload NuevoPayload(Guid cfdiId, string uuid) =>
        new(
            EmpresaId: Guid.NewGuid(),
            OcurridoEn: DateTimeOffset.UtcNow,
            CfdiRecibidoId: cfdiId,
            UuidCfdi: uuid,
            RfcEmisor: "AAA010101AAA");

    private static Guid AgregarRecepcionRegistrada(
        AlmacenDbContext db, string uuidFiscal, Guid? cfdiRecibidoId, int folio)
    {
        var mov = new MovimientoInventario(
            id: Guid.NewGuid(),
            tipo: TipoMovimiento.EntradaCompra,
            empresaId: Guid.NewGuid(),
            fechaMovimiento: new DateOnly(2026, 7, 14));
        mov.AgregarLinea(new LineaMovimiento(
            Guid.NewGuid(), mov.Id, 1, Guid.NewGuid(), 1m, "PZA", 10m));

        // VincularRecepcionVarianteA es internal — reflexión, mismo patrón
        // que ListarRecepcionesHandlerTests.
        typeof(MovimientoInventario)
            .GetMethod("VincularRecepcionVarianteA",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(mov, new object?[] { Guid.NewGuid(), null, cfdiRecibidoId, uuidFiscal });

        mov.Registrar(
            FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2026, folio),
            registradoPor: Guid.NewGuid());

        db.Movimientos.Add(mov);
        return mov.Id;
    }

    private static async Task<AlmacenDbContext> NuevaDbAsync()
    {
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-cfdi-enlace-{Guid.NewGuid():N}")
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
