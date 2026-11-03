using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.EventListeners;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Application.Integration;

namespace Millet.Facturacion.UnitTests.Integration;

/// <summary>
/// F6.3: contratos del reembolso de NC en los dos sentidos (round-trip con las opciones del outbox y del listener) y el
/// escucha de Facturación que actualiza la NC con el estado que reporta Tesorería.
/// </summary>
public sealed class ReembolsoNotaCreditoContratoTests
{
    private static readonly JsonSerializerOptions OutboxJson = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions ListenerJson = new() { PropertyNameCaseInsensitive = true };
    private static readonly DateTimeOffset Ahora = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _empresa = Guid.NewGuid();

    private static TDestino RoundTrip<TDestino>(object evento) =>
        JsonSerializer.Deserialize<TDestino>(JsonSerializer.Serialize(evento, evento.GetType(), OutboxJson), ListenerJson)
        ?? throw new InvalidOperationException("Payload null tras round-trip.");

    [Fact]
    public void ReembolsoSolicitado_lleva_la_cuenta_destino_hasta_tesoreria()
    {
        var evento = new NotaCreditoReembolsoSolicitadoIntegrationEvent(_empresa, Ahora, Guid.NewGuid(), "NC-9", "UUID-9",
            Guid.NewGuid(), "AAA010101AAA", 116m, 116m, "MXN", Guid.NewGuid(), "Devolución", Guid.NewGuid(),
            DestinoBanco: "BBVA", DestinoClabe: "032180000118359719", DestinoTitular: "Cliente SA");

        var espejo = RoundTrip<NotaCreditoReembolsoSolicitadoPayload>(evento);

        espejo.NotaCreditoId.Should().Be(evento.NotaCreditoId);
        espejo.DestinoBanco.Should().Be("BBVA");
        espejo.DestinoClabe.Should().Be("032180000118359719");
        espejo.DestinoTitular.Should().Be("Cliente SA");
        NotaCreditoReembolsoSolicitadoPayload.EventType.Should().Be(evento.EventType);
    }

    [Fact]
    public void ReembolsoActualizado_v1_es_byte_compatible_con_el_espejo()
    {
        var evento = new ReembolsoNotaCreditoActualizadoIntegrationEvent(_empresa, Ahora, Guid.NewGuid(), "DEV-0001",
            Guid.NewGuid(), "Pagada", 116m, "MXN", new DateOnly(2026, 10, 31), "SPEI 1", null);

        var espejo = RoundTrip<ReembolsoNotaCreditoActualizadoPayload>(evento);

        espejo.Should().BeEquivalentTo(new ReembolsoNotaCreditoActualizadoPayload(evento.EmpresaId, evento.OcurridoEn,
            evento.DevolucionId, evento.DevolucionFolio, evento.NotaCreditoId, evento.Estado, evento.Importe, evento.Moneda,
            evento.FechaPago, evento.ReferenciaBancaria, evento.Motivo));
        ReembolsoNotaCreditoActualizadoPayload.EventType.Should().Be(ReembolsoNotaCreditoActualizadoIntegrationEvent.WireEventType);
    }

    private FacturacionDbContext Db() => new(new DbContextOptionsBuilder<FacturacionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(_empresa));

    private NotaCredito NcConReembolso(FacturacionDbContext db)
    {
        var nc = NotaCredito.CrearBonificacion(_empresa, "NC-9", 9, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "76120"), "03", "MXN", null, 2026, 10, 1,
            Guid.NewGuid(), 116m, 0.16m, null);
        nc.MarcarTimbradoEnProceso();
        nc.MarcarTimbrado("55555555-5555-5555-5555-555555555555", null, null, null, Ahora, null, null);
        nc.SolicitarReembolso(116m, Ahora);
        db.NotasCredito.Add(nc);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return nc;
    }

    private ReembolsoNotaCreditoActualizadoPayload Payload(Guid ncId, string estado, DateOnly? fecha = null, string? referencia = null) =>
        new(_empresa, Ahora, Guid.NewGuid(), "DEV-0001", ncId, estado, 116m, "MXN", fecha, referencia, null);

    [Fact]
    public async Task F63a_el_pago_en_tesoreria_deja_la_nc_pagada_con_fecha_y_referencia_una_sola_vez()
    {
        using var db = Db();
        var nc = NcConReembolso(db);
        var handler = new ActualizarReembolsoNotaCreditoHandler(db, new FakeClock(Ahora));
        var eventoId = Guid.NewGuid();
        var pagada = Payload(nc.Id, "Pagada", new DateOnly(2026, 10, 31), "SPEI 1");

        await handler.Handle(new ActualizarReembolsoNotaCreditoCommand(Guid.NewGuid(), Payload(nc.Id, "Autorizada")), default);
        await handler.Handle(new ActualizarReembolsoNotaCreditoCommand(eventoId, pagada), default);
        await handler.Handle(new ActualizarReembolsoNotaCreditoCommand(eventoId, pagada), default);

        var actual = await db.NotasCredito.AsNoTracking().SingleAsync();
        actual.EstadoReembolso.Should().Be(EstadoReembolsoNotaCredito.Pagado);
        actual.ReembolsoPagadoEl.Should().Be(new DateOnly(2026, 10, 31));
        actual.ReembolsoReferencia.Should().Be("SPEI 1");
        (await db.EventosProcesados.CountAsync()).Should().Be(2, "el evento repetido se descarta");
    }

    [Fact]
    public async Task Una_nc_que_no_existe_solo_se_registra()
    {
        using var db = Db();
        var handler = new ActualizarReembolsoNotaCreditoHandler(db, new FakeClock(Ahora));

        await handler.Handle(new ActualizarReembolsoNotaCreditoCommand(Guid.NewGuid(), Payload(Guid.NewGuid(), "Pagada")), default);

        (await db.EventosProcesados.CountAsync()).Should().Be(1);
    }
}
