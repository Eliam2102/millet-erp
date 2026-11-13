using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.SharedKernel.Application.Integration.Tablero;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.SharedKernel.UnitTests.Outbox;

/// <summary>
/// I13.6 · el outbox de un módulo en el tablero de integraciones: pendiente, en
/// reintento, en error (agotó intentos, el worker ya no lo toma) y publicado; el
/// reintento manual devuelve la fila a la cola. Datos ficticios.
/// </summary>
public sealed class OutboxFuenteTableroTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Clasifica_cada_evento_y_pone_primero_los_que_piden_atencion()
    {
        await using var db = NewDb();
        var pendiente = Evento("compras.requisicion.autorizada.v1", Ahora.AddMinutes(-30));
        var reintentando = Evento("compras.orden-compra.autorizada.v1", Ahora.AddMinutes(-20), fallas: 2);
        var enError = Evento("compras.orden-compra.cancelada.v1", Ahora.AddHours(-2), fallas: 4);
        var publicado = Evento("compras.orden-compra.cerrada.v1", Ahora.AddHours(-3));
        publicado.MarcarPublicado(Ahora.AddHours(-1));
        var viejo = Evento("compras.orden-compra.cerrada.v1", Ahora.AddDays(-3));
        viejo.MarcarPublicado(Ahora.AddDays(-2));
        db.AddRange(pendiente, reintentando, enError, publicado, viejo);
        await db.SaveChangesAsync();

        var r = await Fuente(db).LeerAsync(Ahora, 10, default);

        (r.Pendientes, r.Reintentando, r.EnError, r.OkUltimas24h).Should().Be((1, 1, 1, 1));
        r.UltimoExito.Should().Be(Ahora.AddHours(-1));
        r.PendienteMasAntiguo.Should().Be(Ahora.AddMinutes(-30), "el que agotó intentos no cuenta para «detenido»: ya es error");
        r.Mensajes.Select(m => m.Estado).Should().Equal(
            EstadoMensajeIntegracion.Error, EstadoMensajeIntegracion.Reintentando, EstadoMensajeIntegracion.Pendiente,
            EstadoMensajeIntegracion.Ok, EstadoMensajeIntegracion.Ok);
        r.Mensajes[0].Should().Match<MensajeIntegracion>(m =>
            m.Referencia == "compras.orden-compra.cancelada.v1" && m.Intentos == 4 && m.UltimoError == "Timeout");
    }

    [Fact]
    public async Task El_reintento_manual_regresa_el_evento_a_la_cola_y_no_toca_uno_ya_publicado()
    {
        await using var db = NewDb();
        var enError = Evento("compras.orden-compra.cancelada.v1", Ahora.AddHours(-2), fallas: 4);
        var publicado = Evento("compras.orden-compra.cerrada.v1", Ahora.AddHours(-3));
        publicado.MarcarPublicado(Ahora.AddHours(-1));
        db.AddRange(enError, publicado);
        await db.SaveChangesAsync();
        var fuente = Fuente(db);

        (await fuente.ReintentarAsync(enError.Id.ToString(), default)).Should().BeTrue();
        (await fuente.ReintentarAsync(publicado.Id.ToString(), default)).Should().BeFalse();
        (await fuente.ReintentarAsync("no-es-guid", default)).Should().BeFalse();

        var fila = await db.Set<IntegrationEventOutboxEntry>().AsNoTracking().SingleAsync(e => e.Id == enError.Id);
        (fila.Attempts, fila.PublishedAt, fila.LastError).Should().Be((0, null, "Timeout"));
        (await fuente.LeerAsync(Ahora, 10, default)).EnError.Should().Be(0);
    }

    [Fact]
    public void Clave_y_nombre_salen_del_contexto_y_del_topic()
    {
        using var db = NewDb();
        var f = Fuente(db);
        (f.Clave, f.Nombre, f.Grupo, f.PermiteReintento).Should().Be(
            ("outbox.comprasprueba", "Eventos de Compras (compras-events)", "Service Bus", true));
    }

    // ---------- Apoyo ----------

    /// <summary>MaxAttempts = 3: con 4 fallas el worker ya no lo toma.</summary>
    private static OutboxFuenteTablero<ComprasPruebaDbContext> Fuente(ComprasPruebaDbContext db) =>
        new(db, new Monitor(new OutboxPublisherOptions { MaxAttempts = 3, ServiceBusTopicName = "compras-events" }), "Compras");

    private static IntegrationEventOutboxEntry Evento(string tipo, DateTimeOffset ocurrio, int fallas = 0)
    {
        var e = new IntegrationEventOutboxEntry(Guid.NewGuid(), tipo, "{}", ocurrio, Guid.NewGuid());
        for (var i = 0; i < fallas; i++) e.RegistrarFalla("Timeout");
        return e;
    }

    private static ComprasPruebaDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ComprasPruebaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public sealed class ComprasPruebaDbContext(DbContextOptions<ComprasPruebaDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<IntegrationEventOutboxEntry>(e =>
            {
                e.HasKey(x => x.Id);
                e.Ignore(x => x.Version);
            });
    }

    private sealed class Monitor(OutboxPublisherOptions valor) : IOptionsMonitor<OutboxPublisherOptions>
    {
        public OutboxPublisherOptions CurrentValue => valor;
        public OutboxPublisherOptions Get(string? name) => valor;
        public IDisposable? OnChange(Action<OutboxPublisherOptions, string?> listener) => null;
    }
}
