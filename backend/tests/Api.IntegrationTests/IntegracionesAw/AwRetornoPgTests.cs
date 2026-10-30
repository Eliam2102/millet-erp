using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.Integraciones.Aw.Infrastructure.Retorno;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure;
using Npgsql;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>TR-01 · filas Documento de aw_retorno contra la copia desechable de demo (PostgreSQL), mismo SQL que Real.</summary>
[Collection("AwClientesSync")]
public class AwRetornoPgTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private string _cs = "";
    private readonly Versiones _versiones = new();

    public async Task InitializeAsync()
    {
        _cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")!;
        await EjecutarAsync("DROP SCHEMA IF EXISTS dbo CASCADE; DROP SCHEMA IF EXISTS aw_origen CASCADE");
        await EjecutarAsync(await File.ReadAllTextAsync(Path.Combine(AwOrigenPgTests.DirectorioOrigenDemo(), "schema.sql")));
    }

    public async Task DisposeAsync() =>
        await EjecutarAsync("DROP SCHEMA IF EXISTS dbo CASCADE; DROP SCHEMA IF EXISTS aw_origen CASCADE");

    private sealed class Versiones : IAwVersionPedidoReadPort
    {
        public Dictionary<string, long> PorPedido { get; } = [];
        public Task<long?> VersionAplicadaAsync(string numeroPedido, CancellationToken ct) =>
            Task.FromResult(PorPedido.TryGetValue(numeroPedido, out var v) ? (long?)v : null);
    }

    private AwRetornoAdapter Adapter() => new(new AwOrigenPg.Fabrica(_cs),
        Options.Create(new AwPedidosOptions { SqlConnectTimeoutSeconds = 5, SqlQueryTimeoutSeconds = 15 }),
        AwRetornoAdapter.CandadoPostgres);

    private AwRetornoDocumentoService Servicio() =>
        new(Adapter(), _versiones, NullLogger<AwRetornoDocumentoService>.Instance);

    private static AwRetornoDocumento Doc(string pedido, string uuid, long version = 1, decimal importe = 1160m, string folio = "F-1") =>
        new(pedido, "FacturaFinal", version, folio, uuid, importe);

    private async Task EjecutarAsync(string sql, params (string, object?)[] ps)
    {
        await using var cn = new NpgsqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> FilasAsync(string pedido)
    {
        await using var cn = new NpgsqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT concat_ws('|', secuencia, tipo, version_pedido, tipo_documento, folio, uuid, importe, COALESCE(resultado::text, '-'), intentos) " +
            "FROM dbo.aw_retorno WHERE numero_pedido = @p ORDER BY secuencia", cn);
        cmd.Parameters.AddWithValue("p", pedido);
        var filas = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) filas.Add(r.GetString(0));
        return filas;
    }

    // ===== Criterio 1: una fila Documento por factura =====
    [Fact]
    public async Task Cada_factura_del_pedido_deja_una_fila_Documento_con_su_secuencia()
    {
        var adapter = Adapter();
        (await adapter.AgregarDocumentoAsync(Doc("900101", "UUID-A", version: 3, importe: 580m, folio: "F-10"), default)).Should().Be(1);
        (await adapter.AgregarDocumentoAsync(Doc("900101", "UUID-B", version: 3, importe: 580m, folio: "F-11"), default)).Should().Be(2);
        (await adapter.AgregarDocumentoAsync(Doc("900202", "UUID-C"), default)).Should().Be(1); // la secuencia es por pedido

        (await FilasAsync("900101")).Should().Equal("1|3|3|FacturaFinal|F-10|UUID-A|580.00|-|0", "2|3|3|FacturaFinal|F-11|UUID-B|580.00|-|0");
        (await FilasAsync("900202")).Should().HaveCount(1);
    }

    // ===== Criterio 1 (disparo): evento de Facturación → fila =====
    [Fact]
    public async Task El_evento_de_factura_timbrada_se_convierte_en_fila_y_un_pedido_no_AW_se_ignora()
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        servicios.AddSingleton<ICurrentEmpresaContext>(new EmpresaBypass());
        servicios.AddScoped(_ => Servicio());
        await using var sp = servicios.BuildServiceProvider();
        ILocalIntegrationEventConsumer listener = new FacturacionRetornoListenerWorker(null!, sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<FacturacionRetornoListenerWorker>.Instance);

        // Mismo JSON (PascalCase) que serializa el outbox.
        var evento = new Millet.Facturacion.Application.Integration.FacturaVentaTimbradaIntegrationEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), "UUID-EVT", 2320m, "MXN", Guid.NewGuid(),
            "XAXX010101000", "Cliente", "F-77", "PPD", DateTimeOffset.UtcNow, NumeroPedidoAw: "900303", VersionPedidoAw: 4);
        var json = JsonSerializer.Serialize(evento);

        await listener.ConsumirLocalAsync(FacturacionRetornoListenerWorker.EventTypeFacturaTimbrada, Guid.NewGuid(), json, default);
        await listener.ConsumirLocalAsync(FacturacionRetornoListenerWorker.EventTypeFacturaTimbrada, Guid.NewGuid(), json, default); // reentrega
        await listener.ConsumirLocalAsync(FacturacionRetornoListenerWorker.EventTypeFacturaTimbrada, Guid.NewGuid(), JsonSerializer.Serialize(evento with { NumeroPedidoAw = null, VersionPedidoAw = null }), default);
        await listener.ConsumirLocalAsync("facturacion.recibo-pago.timbrado.v1", Guid.NewGuid(), "{}", default);

        (await FilasAsync("900303")).Should().Equal("1|3|4|FacturaFinal|F-77|UUID-EVT|2320.00|-|0");
    }

    private (ILocalIntegrationEventConsumer Listener, ServiceProvider Sp) ListenerConServicio()
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        servicios.AddSingleton<ICurrentEmpresaContext>(new EmpresaBypass());
        servicios.AddScoped(_ => Servicio());
        var sp = servicios.BuildServiceProvider();
        return (new FacturacionRetornoListenerWorker(null!, sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<FacturacionRetornoListenerWorker>.Instance), sp);
    }

    private static string Timbrada(string uuid, string folio, string pedido, long version) => JsonSerializer.Serialize(
        new Millet.Facturacion.Application.Integration.FacturaVentaTimbradaIntegrationEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), uuid, 580m, "MXN", Guid.NewGuid(),
            "XAXX010101000", "Cliente", folio, "PPD", DateTimeOffset.UtcNow, NumeroPedidoAw: pedido, VersionPedidoAw: version));

    private static string Cancelada(string uuid, string folio, string pedido, long version) => JsonSerializer.Serialize(
        new Millet.Facturacion.Application.Integration.ComprobanteCanceladoIntegrationEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), "FacturaVenta", uuid,
            NumeroPedidoAw: pedido, VersionPedidoAw: version, Folio: folio, Total: 580m));

    // ===== Criterio 3 (ficha): cancelar A deja fila nueva de A; B no cambia; repetir el evento no duplica =====
    [Fact]
    public async Task Cancelar_la_factura_A_deja_una_fila_Cancelacion_de_A_y_la_de_B_no_cambia()
    {
        var (listener, sp) = ListenerConServicio();
        await using var _ = sp;
        await listener.ConsumirLocalAsync(FacturacionRetornoListenerWorker.EventTypeFacturaTimbrada, Guid.NewGuid(), Timbrada("UUID-A", "F-A", "900101", 2), default);
        await listener.ConsumirLocalAsync(FacturacionRetornoListenerWorker.EventTypeFacturaTimbrada, Guid.NewGuid(), Timbrada("UUID-B", "F-B", "900101", 2), default);
        var antes = await FilasAsync("900101");

        var cancelacion = Cancelada("UUID-A", "F-A", "900101", 2);
        await listener.ConsumirLocalAsync(FacturacionRetornoListenerWorker.EventTypeComprobanteCancelado, Guid.NewGuid(), cancelacion, default);
        await listener.ConsumirLocalAsync(FacturacionRetornoListenerWorker.EventTypeComprobanteCancelado, Guid.NewGuid(), cancelacion, default); // reentrega
        await listener.ConsumirLocalAsync(FacturacionRetornoListenerWorker.EventTypeComprobanteCancelado, Guid.NewGuid(),
            JsonSerializer.Serialize(new { Uuid = "UUID-X", TipoComprobante = "FacturaAnticipo" }), default); // no es de pedido A+W

        (await FilasAsync("900101")).Should().Equal(antes[0], antes[1], "3|3|2|Cancelacion|F-A|UUID-A|580.00|-|0");
    }

    // ===== Criterio 5 (ficha): demo encendido, se timbra una parcialidad → la fila aparece en < 1 min =====
    [Fact]
    public async Task En_el_canal_demo_la_fila_de_la_parcialidad_timbrada_aparece_en_menos_de_un_minuto()
    {
        var (listener, sp) = ListenerConServicio();
        await using var _ = sp;
        var reloj = System.Diagnostics.Stopwatch.StartNew();

        await listener.ConsumirLocalAsync(FacturacionRetornoListenerWorker.EventTypeFacturaTimbrada, Guid.NewGuid(), Timbrada("UUID-P1", "F-P1", "900404", 1), default);
        while ((await FilasAsync("900404")).Count == 0 && reloj.Elapsed < TimeSpan.FromSeconds(60)) await Task.Delay(200);

        (await FilasAsync("900404")).Should().HaveCount(1);
        reloj.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60));
    }

    private sealed class EmpresaBypass : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Nada();
        private sealed class Nada : IDisposable { public void Dispose() { } }
    }

    // ===== Criterio 2: idempotente por UUID + operación, sin sobrescribir, candado por pedido =====
    [Fact]
    public async Task Repetir_el_mismo_UUID_no_duplica_ni_sobrescribe_la_fila()
    {
        var adapter = Adapter();
        await adapter.AgregarDocumentoAsync(Doc("900101", "UUID-A", importe: 580m), default);
        await EjecutarAsync("UPDATE dbo.aw_retorno SET resultado = 1, motivo = 'ok', aplicada_at = now() WHERE uuid = 'UUID-A'"); // TI ya la aplicó

        (await adapter.AgregarDocumentoAsync(Doc("900101", "UUID-A", importe: 999m, folio: "OTRO"), default)).Should().BeNull();

        (await FilasAsync("900101")).Should().Equal("1|3|1|FacturaFinal|F-1|UUID-A|580.00|1|0");
    }

    [Fact]
    public async Task El_mismo_UUID_con_otra_operacion_si_es_otra_fila()
    {
        var adapter = Adapter();
        await adapter.AgregarDocumentoAsync(Doc("900101", "UUID-A"), default);
        (await adapter.AgregarDocumentoAsync(Doc("900101", "UUID-A") with { TipoDocumento = "NotaCredito" }, default)).Should().Be(2);
    }

    [Fact]
    public async Task Entregas_simultaneas_del_mismo_pedido_no_duplican_y_la_secuencia_no_se_repite()
    {
        // 6 facturas distintas, cada una entregada 3 veces a la vez.
        var tareas = Enumerable.Range(0, 18).Select(i => Task.Run(() =>
            Adapter().AgregarDocumentoAsync(Doc("900101", $"UUID-{i % 6}", folio: $"F-{i % 6}"), default)));
        var secuencias = await Task.WhenAll(tareas);

        secuencias.Where(s => s is not null).Select(s => s!.Value).Order().Should().Equal(1, 2, 3, 4, 5, 6);
        (await FilasAsync("900101")).Should().HaveCount(6);
    }

    // ===== Criterio 3: lee el acuse; desfasada → fila nueva con la versión siguiente =====
    [Fact]
    public async Task Una_fila_desfasada_se_reemplaza_con_una_fila_nueva_con_la_version_del_ERP()
    {
        var adapter = Adapter();
        await adapter.AgregarDocumentoAsync(Doc("900101", "UUID-A", version: 5, importe: 580m, folio: "F-10"), default);
        await EjecutarAsync("UPDATE dbo.aw_retorno SET resultado = 3, motivo = 'A+W ya tiene la versión 6', aplicada_at = now(), intentos = 1");

        // El ERP todavía no aplica una versión mayor: espera, no inventa una fila.
        _versiones.PorPedido["900101"] = 5;
        (await Servicio().ConciliarAcusesAsync(default)).Should().Be(new AwRetornoConciliacion(0, 1, 0));
        (await FilasAsync("900101")).Should().HaveCount(1);

        // El ERP ya aplicó la 7: fila nueva (secuencia 2) con la versión nueva; la desfasada no se toca.
        _versiones.PorPedido["900101"] = 7;
        (await Servicio().ConciliarAcusesAsync(default)).Should().Be(new AwRetornoConciliacion(1, 0, 0));
        (await FilasAsync("900101")).Should().Equal("1|3|5|FacturaFinal|F-10|UUID-A|580.00|3|1", "2|3|7|FacturaFinal|F-10|UUID-A|580.00|-|0");

        // Otra pasada, o la reentrega del evento viejo, no agrega nada.
        (await Servicio().ConciliarAcusesAsync(default)).Should().Be(new AwRetornoConciliacion(0, 0, 0));
        (await adapter.AgregarDocumentoAsync(Doc("900101", "UUID-A", version: 5), default)).Should().BeNull();
        (await FilasAsync("900101")).Should().HaveCount(2);
    }

    [Fact]
    public async Task El_acuse_se_lee_completo_y_solo_las_detenidas_o_desfasadas_piden_atencion()
    {
        var adapter = Adapter();
        foreach (var u in new[] { "U-APLICADA", "U-RECHAZO-2", "U-RECHAZO-3", "U-PENDIENTE", "U-DESFASADA" })
            await adapter.AgregarDocumentoAsync(Doc("900101", u, version: 2), default);
        await EjecutarAsync("UPDATE dbo.aw_retorno SET resultado = 1, aplicada_at = now() WHERE uuid = 'U-APLICADA'");
        await EjecutarAsync("UPDATE dbo.aw_retorno SET resultado = 2, motivo = 'A+W rechazó', intentos = 2 WHERE uuid = 'U-RECHAZO-2'");
        await EjecutarAsync("UPDATE dbo.aw_retorno SET resultado = 2, motivo = 'A+W rechazó', intentos = 3 WHERE uuid = 'U-RECHAZO-3'");
        await EjecutarAsync("UPDATE dbo.aw_retorno SET resultado = 3, motivo = 'versión mayor', aplicada_at = now(), intentos = 1 WHERE uuid = 'U-DESFASADA'");

        var acuses = await Adapter().LeerAcusesPorAtenderAsync(default);

        acuses.Select(a => a.Uuid).Should().BeEquivalentTo("U-RECHAZO-3", "U-DESFASADA");
        var detenida = acuses.Single(a => a.Uuid == "U-RECHAZO-3");
        (detenida.Resultado, detenida.Motivo, detenida.Intentos, detenida.Detenida).Should().Be(((byte?)2, "A+W rechazó", 3, true));
        var desfasada = acuses.Single(a => a.Uuid == "U-DESFASADA");
        (desfasada.Resultado, desfasada.Intentos, desfasada.Desfasada).Should().Be(((byte?)3, 1, true));
        desfasada.AplicadaAt.Should().NotBeNull();

        // Tres rechazos: solo se reporta, no se reemplaza ni se reintenta desde el ERP.
        var r = await Servicio().ConciliarAcusesAsync(default);
        r.Detenidas.Should().Be(1);
        (await FilasAsync("900101")).Should().HaveCount(5);
    }

    // ===== Criterio 4: escritor viejo apagado =====
    [Fact]
    public async Task El_write_back_por_pedido_del_adapter_viejo_ya_no_escribe_nada()
    {
        await EjecutarAsync("""
            INSERT INTO dbo.aw_solicitud_pedido (solicitud_id, numero_pedido, operacion, version)
            VALUES (gen_random_uuid(), '900101', 1, 1), (gen_random_uuid(), '900101', 2, 2)
            """);
        var viejo = new AwWriteBackPgAdapter(new AwOrigenPg.Fabrica(_cs),
            Options.Create(new AwPedidosOptions { SqlConnectTimeoutSeconds = 5, SqlQueryTimeoutSeconds = 15 }),
            NullLogger<AwWriteBackPgAdapter>.Instance);

        await viejo.EscribirResultadoAsync(new AwWriteBack(Guid.Empty, "900101", null, "Facturado", "UUID-A",
            ResultadoSolicitudAw.Aplicada, null), default);

        await using var cn = new NpgsqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM dbo.aw_solicitud_pedido WHERE uuid IS NOT NULL OR estado_facturacion IS NOT NULL", cn);
        ((long)(await cmd.ExecuteScalarAsync())!).Should().Be(0);
    }

    // ===== Criterio 5: misma tabla en el canal demo =====
    [Fact]
    public async Task La_tabla_demo_tiene_las_restricciones_del_script_14()
    {
        await EjecutarAsync("INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, uuid) VALUES ('P', 1, 3, 1, 'FacturaFinal', 'U')");

        // Documento sin uuid, tipo de documento inválido, secuencia repetida por pedido, intentos negativos.
        var malas = new[]
        {
            "INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento) VALUES ('P', 2, 3, 1, 'FacturaFinal')",
            "INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, uuid) VALUES ('P', 2, 3, 1, 'Recibo', 'U2')",
            "INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, uuid) VALUES ('P', 1, 3, 1, 'FacturaFinal', 'U3')",
            "INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, uuid, intentos) VALUES ('P', 3, 3, 1, 'FacturaFinal', 'U4', -1)",
            // v3: un pago exige folio, forma de pago, moneda e importe.
            "INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, folio, importe) VALUES ('P', 4, 4, 1, 'F-1', 100)",
        };
        await EjecutarAsync("INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, folio, forma_pago, moneda, importe) VALUES ('P', 5, 4, 1, 'F-1', '03', 'MXN', 100)");
        foreach (var sql in malas)
            await FluentActions.Awaiting(() => EjecutarAsync(sql)).Should().ThrowAsync<PostgresException>();
    }
}
