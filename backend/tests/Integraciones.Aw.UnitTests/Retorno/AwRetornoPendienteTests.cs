using System.Net.Sockets;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Infrastructure.Retorno;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.UnitTests.Retorno;

/// <summary>M-160: con A+W caído el aviso queda pendiente, se reintenta con espera creciente y se aplica una sola vez.</summary>
public sealed class AwRetornoPendienteTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    // ---------- Espera creciente ----------
    [Fact]
    public void Espera_es_1_5_15_60_minutos_y_luego_cada_hora_sin_tope()
    {
        var p = AwRetornoPendiente.Crear("t", "{}", "caído", T0);
        p.Intentos.Should().Be(1);
        p.ProximoIntentoUtc.Should().Be(T0.AddMinutes(1));
        var esperadas = new[] { 5, 15, 60, 60, 60 };
        for (var i = 0; i < esperadas.Length; i++)
        {
            p.RegistrarFallo("caído", T0);
            p.Intentos.Should().Be(i + 2);
            p.ProximoIntentoUtc.Should().Be(T0.AddMinutes(esperadas[i]));
        }
        p.Estado.Should().Be(AwRetornoPendiente.EstadoPendiente);
    }

    // ---------- Clasificación conexión vs payload ----------
    [Fact]
    public void Clasifica_fallas_de_conexion_y_deja_fuera_las_del_aviso()
    {
        AwRetornoAplicador.EsDeConexion(new TimeoutException()).Should().BeTrue();
        AwRetornoAplicador.EsDeConexion(new SocketException()).Should().BeTrue();
        AwRetornoAplicador.EsDeConexion(new InvalidOperationException("x", new IOException())).Should().BeTrue();
        AwRetornoAplicador.EsDeConexion(new JsonException()).Should().BeFalse();
        AwRetornoAplicador.EsDeConexion(new BusinessRuleException("AW_RETORNO_MAPEO_PAGO", "x")).Should().BeFalse();
        AwRetornoAplicador.EsDeConexion(new InvalidOperationException()).Should().BeFalse();
    }

    [Fact]
    public async Task SqlServer_inalcanzable_cuenta_como_falla_de_conexion()
    {
        await using var cn = new SqlConnection("Server=tcp:127.0.0.1,1;Connect Timeout=2;ConnectRetryCount=0;Encrypt=false");
        var ex = await Record.ExceptionAsync(() => cn.OpenAsync());
        ex.Should().BeOfType<SqlException>();
        AwRetornoAplicador.EsDeConexion(ex).Should().BeTrue();
    }

    // ---------- Flujo: pendiente, reintento, aplicado una vez ----------
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow { get; set; } = T0; }

    private sealed class Empresas : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Nada();
        private sealed class Nada : IDisposable { public void Dispose() { } }
    }

    /// <summary>aw_retorno falso: con <see cref="Caido"/> falla como red; idempotente por UUID + tipo como el real.</summary>
    private sealed class Puerto : IAwRetornoPort
    {
        public bool Caido { get; set; }
        public List<AwRetornoDocumento> Filas { get; } = [];
        public Task<int?> AgregarDocumentoAsync(AwRetornoDocumento d, CancellationToken ct)
        {
            if (Caido) throw new TimeoutException("A+W no responde");
            if (Filas.Any(f => f.Uuid == d.Uuid && f.TipoDocumento == d.TipoDocumento)) return Task.FromResult<int?>(null);
            Filas.Add(d);
            return Task.FromResult<int?>(Filas.Count);
        }
        public Task<int> AgregarPagosAsync(string a, long b, string c, IReadOnlyList<AwPagoGrupo> d, bool e, CancellationToken ct) => Task.FromResult(0);
        public Task<int?> AgregarEstatusAsync(string a, long b, short c, byte? d, string? e, CancellationToken ct) => Task.FromResult<int?>(null);
        public Task<int?> AgregarSaldoAsync(string a, long b, decimal c, CancellationToken ct) => Task.FromResult<int?>(null);
        public Task<IReadOnlyList<AwRetornoAcuse>> LeerAcusesPorAtenderAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AwRetornoAcuse>>([]);
    }

    private sealed class SinVersion : IAwVersionPedidoReadPort
    {
        public Task<long?> VersionAplicadaAsync(string numeroPedido, CancellationToken ct) => Task.FromResult<long?>(null);
    }

    private sealed class SinPagos : IAwPagosReadPort
    {
        public Task<IReadOnlyList<PagosFacturaAw>> PorComprobanteAsync(Guid comprobanteId, CancellationToken ct) => Task.FromResult<IReadOnlyList<PagosFacturaAw>>([]);
        public Task<PagosFacturaAw?> PorFolioAsync(string numeroPedidoAw, string folio, CancellationToken ct) => Task.FromResult<PagosFacturaAw?>(null);
    }

    private sealed record Escenario(ServiceProvider Sp, Puerto Puerto, Reloj Reloj, FacturacionRetornoListenerWorker Oyente, AwRetornoAcuseWorker Reintento)
    {
        public List<AwRetornoPendiente> Pendientes()
        {
            using var scope = Sp.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().RetornoPendientes.AsNoTracking().ToList();
        }
    }

    private static Escenario Crear()
    {
        var puerto = new Puerto();
        var reloj = new Reloj();
        var db = "retorno-pendiente-" + Guid.NewGuid().ToString("N");
        var s = new ServiceCollection();
        s.AddDbContext<IntegracionesAwDbContext>(o => o.UseInMemoryDatabase(db));
        s.AddSingleton<ICurrentEmpresaContext, Empresas>();
        s.AddSingleton<IClock>(reloj);
        s.AddScoped<AwRetornoPendientes>();
        s.AddScoped(_ => new AwRetornoDocumentoService(puerto, new SinVersion(), new SinPagos(),
            Options.Create(new AwPedidosOptions()), NullLogger<AwRetornoDocumentoService>.Instance));
        var sp = s.BuildServiceProvider();
        var scopes = sp.GetRequiredService<IServiceScopeFactory>();
        return new Escenario(sp, puerto, reloj,
            new FacturacionRetornoListenerWorker(null!, scopes, NullLogger<FacturacionRetornoListenerWorker>.Instance),
            new AwRetornoAcuseWorker(scopes, NullLogger<AwRetornoAcuseWorker>.Instance));
    }

    private static string Factura(string uuid) =>
        JsonSerializer.Serialize(new FacturaTimbradaRetornoPayload(uuid, 100m, "F-" + uuid, "900101", 1));

    [Fact]
    public async Task Con_AW_caido_el_aviso_queda_pendiente_se_reintenta_y_al_volver_se_aplica_una_sola_vez()
    {
        var e = Crear();
        e.Puerto.Caido = true;

        // El evento no falla (sin Service Bus no relanza: el mensaje se daría por completado) y queda el pendiente.
        await e.Oyente.ProcessAsync(FacturacionRetornoListenerWorker.EventTypeFacturaTimbrada, Factura("U-1"), null, default);
        var p = e.Pendientes().Should().ContainSingle().Subject;
        p.Estado.Should().Be(AwRetornoPendiente.EstadoPendiente);
        p.Intentos.Should().Be(1);
        p.UltimoError.Should().Contain("A+W no responde");
        p.ProximoIntentoUtc.Should().Be(T0.AddMinutes(1));

        // Antes de vencer no se toca.
        (await e.Reintento.ReintentarPendientesOnceAsync(default)).Should().Be(0);
        e.Pendientes().Single().Intentos.Should().Be(1);

        // Vence y A+W sigue caído: intento 2, siguiente en 5 min.
        e.Reloj.UtcNow = T0.AddMinutes(1);
        await e.Reintento.ReintentarPendientesOnceAsync(default);
        p = e.Pendientes().Single();
        p.Intentos.Should().Be(2);
        p.ProximoIntentoUtc.Should().Be(T0.AddMinutes(6));

        // A+W vuelve: se aplica, queda Aplicado y la fila se escribe una vez.
        e.Puerto.Caido = false;
        e.Reloj.UtcNow = T0.AddMinutes(6);
        (await e.Reintento.ReintentarPendientesOnceAsync(default)).Should().Be(1);
        p = e.Pendientes().Single();
        p.Estado.Should().Be(AwRetornoPendiente.EstadoAplicado);
        p.AplicadoEnUtc.Should().Be(T0.AddMinutes(6));
        e.Puerto.Filas.Should().ContainSingle(f => f.Uuid == "U-1");

        // Una reentrega del mismo evento no duplica y otra pasada no reaplica.
        await e.Oyente.ProcessAsync(FacturacionRetornoListenerWorker.EventTypeFacturaTimbrada, Factura("U-1"), null, default);
        (await e.Reintento.ReintentarPendientesOnceAsync(default)).Should().Be(0);
        e.Puerto.Filas.Should().ContainSingle();
    }

    [Fact]
    public async Task Payload_corrupto_no_queda_pendiente()
    {
        var e = Crear();
        e.Puerto.Caido = true;
        await e.Oyente.ProcessAsync(FacturacionRetornoListenerWorker.EventTypeFacturaTimbrada, "{no es json", null, default);
        e.Pendientes().Should().BeEmpty();
    }

    [Fact]
    public async Task Pendiente_que_falla_por_el_aviso_se_detiene_y_no_se_reintenta()
    {
        var e = Crear();
        using (var scope = e.Sp.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AwRetornoPendientes>()
                .GuardarAsync(FacturacionRetornoListenerWorker.EventTypeFacturaTimbrada, "{roto", "caído", default);
        e.Reloj.UtcNow = T0.AddMinutes(1);
        await e.Reintento.ReintentarPendientesOnceAsync(default);
        e.Pendientes().Single().Estado.Should().Be(AwRetornoPendiente.EstadoDetenido);
    }

    [Fact]
    public async Task Resumen_cuenta_pendientes_muestra_el_mas_antiguo_y_alerta_pasadas_4_horas()
    {
        var e = Crear();
        e.Puerto.Caido = true;
        await e.Oyente.ProcessAsync(FacturacionRetornoListenerWorker.EventTypeFacturaTimbrada, Factura("U-1"), null, default);
        e.Reloj.UtcNow = T0.AddMinutes(10);
        await e.Oyente.ProcessAsync(FacturacionRetornoListenerWorker.EventTypeFacturaTimbrada, Factura("U-2"), null, default);

        using var scope = e.Sp.CreateScope();
        var pendientes = scope.ServiceProvider.GetRequiredService<AwRetornoPendientes>();
        var r = await pendientes.ResumenAsync(default);
        r.Pendientes.Should().Be(2);
        r.MasAntiguoEnUtc.Should().Be(T0);
        r.MasAntiguoUltimoError.Should().Contain("A+W no responde");
        r.Alerta.Should().BeFalse();

        e.Reloj.UtcNow = T0.AddHours(4).AddMinutes(1);
        (await pendientes.ResumenAsync(default)).Alerta.Should().BeTrue();
    }

    // ---------- Reproceso de dead-letter ----------
    private sealed class ReceptorFalso(IEnumerable<ServiceBusReceivedMessage> mensajes) : ServiceBusReceiver
    {
        private readonly Queue<ServiceBusReceivedMessage> _cola = new(mensajes);
        public List<ServiceBusReceivedMessage> Completados { get; } = [];
        public override Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveMessagesAsync(
            int maxMessages, TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = default)
        {
            var lote = new List<ServiceBusReceivedMessage>();
            while (lote.Count < maxMessages && _cola.TryDequeue(out var m)) lote.Add(m);
            return Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(lote);
        }
        public override Task CompleteMessageAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
        {
            Completados.Add(message);
            return Task.CompletedTask;
        }
    }

    private static ServiceBusReceivedMessage Mensaje(string eventType, string body) =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString(body), messageId: Guid.NewGuid().ToString(), subject: eventType);

    [Fact]
    public async Task Reprocesar_dead_letter_aplica_sin_duplicar_y_deja_el_payload_corrupto()
    {
        var e = Crear();
        await e.Oyente.ProcessAsync(FacturacionRetornoListenerWorker.EventTypeFacturaTimbrada, Factura("U-1"), null, default); // ya aplicado antes
        var tipo = FacturacionRetornoListenerWorker.EventTypeFacturaTimbrada;
        var receptor = new ReceptorFalso([Mensaje(tipo, Factura("U-1")), Mensaje(tipo, Factura("U-2")), Mensaje(tipo, Factura("U-3")),
            Mensaje(tipo, "{roto")]);
        var handler = new ReprocesarDeadLetterRetornoAwHandler(e.Sp, e.Sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ReprocesarDeadLetterRetornoAwHandler>.Instance);

        var r = await handler.ReprocesarAsync(receptor, default);

        r.Should().Be(new ReprocesoDeadLetterRetornoAw(4, 3, 0, 1, 0));
        receptor.Completados.Should().HaveCount(3);
        e.Puerto.Filas.Select(f => f.Uuid).Should().BeEquivalentTo(["U-1", "U-2", "U-3"]);
    }

    [Fact]
    public async Task Reprocesar_sin_Service_Bus_responde_regla_de_negocio()
    {
        var e = Crear();
        var handler = new ReprocesarDeadLetterRetornoAwHandler(e.Sp, e.Sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ReprocesarDeadLetterRetornoAwHandler>.Instance);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => handler.Handle(new ReprocesarDeadLetterRetornoAwCommand(), default));
        ex.Code.Should().Be("SERVICE_BUS_NO_CONFIGURADO");
    }
}
