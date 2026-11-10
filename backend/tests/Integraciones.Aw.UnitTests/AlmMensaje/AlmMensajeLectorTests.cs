using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Infrastructure.Workers;
using Millet.Integraciones.Aw.Infrastructure.AlmMensaje;

namespace Millet.Integraciones.Aw.UnitTests.AlmMensaje;

/// <summary>
/// Acuerdo 5-nov con TI: lector de <c>alm_mensaje</c> contra el origen simulado. Caso feliz, duplicado, falla, lento,
/// orden por secuencia y freno por ERROR de la referencia. El manejador real se prueba aparte (Api.IntegrationTests).
/// </summary>
public sealed class AlmMensajeLectorTests
{
    /// <summary>Manejador de mentira, idempotente por mensajeId como el real: el segundo envío es DUPLICADO con el mismo folio.</summary>
    private sealed class Procesador : IProcesadorMensajeApp
    {
        public Dictionary<string, AcuseApp> Hechos { get; } = [];
        public List<string> Aplicados { get; } = [];
        public HashSet<string> Rechazar { get; } = [];
        public HashSet<string> Lanzar { get; } = [];
        public TimeSpan Espera { get; set; }

        public async Task<AcuseApp> ProcesarAsync(JsonElement sobre, CancellationToken ct)
        {
            var id = sobre.GetProperty("mensajeId").GetString()!;
            if (Espera > TimeSpan.Zero) await Task.Delay(Espera, ct);
            if (Lanzar.Contains(id)) throw new InvalidOperationException("falla del ERP");
            if (Hechos.TryGetValue(id, out var previo)) return new AcuseApp(id, "DUPLICADO", previo.Folio, previo.Error);
            Aplicados.Add(id);
            var acuse = Rechazar.Contains(id)
                ? new AcuseApp(id, "ERROR", $"REV-{id}", "Sin existencia suficiente")
                : new AcuseApp(id, "RECIBIDO", $"F-{Aplicados.Count}", null);
            Hechos[id] = acuse;
            return acuse;
        }
    }

    private sealed record Banco(SimuladoAlmMensajeOrigen Origen, Procesador Proc, AlmMensajeLector Lector);

    private static Banco Crear(int maxIntentos = 3)
    {
        var proc = new Procesador();
        var origen = new SimuladoAlmMensajeOrigen();
        var sp = new ServiceCollection().AddSingleton<IProcesadorMensajeApp>(proc).BuildServiceProvider();
        var lector = new AlmMensajeLector(origen, sp.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AlmMensajeOptions { Habilitado = true, MaxIntentos = maxIntentos }), NullLogger<AlmMensajeLector>.Instance);
        return new Banco(origen, proc, lector);
    }

    private static string Sobre(string id) =>
        JsonSerializer.Serialize(new { mensajeId = id, tipo = "SALIDA_MANUAL", version = 1, datos = new { } });
    private static long Poner(Banco b, string id, string? referencia) => b.Origen.Encolar(id, "SALIDA_MANUAL", referencia, Sobre(id));

    [Fact]
    public async Task Feliz_acusa_cada_renglon_en_orden_de_secuencia_con_su_folio()
    {
        var b = Crear();
        Poner(b, "M1", "RQ-1"); Poner(b, "M2", "RQ-2"); Poner(b, "M3", "RQ-1");

        var tick = await b.Lector.ProcesarAsync(default);

        tick.Should().Be(new AlmMensajeTick(3, 0, 0, 0));
        b.Proc.Aplicados.Should().Equal("M1", "M2", "M3");
        b.Origen.AcuseDe("M1")!.Should().Be(new AcuseApp("M1", "RECIBIDO", "F-1", null));
        (await b.Origen.LeerPendientesAsync(10, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicado_el_mismo_mensajeId_dos_veces_acusa_DUPLICADO_con_el_mismo_folio_sin_aplicar_otra_vez()
    {
        var b = Crear();
        Poner(b, "M1", null);
        await b.Lector.ProcesarAsync(default);
        b.Origen.Reprocesar("M1"); // la app lo reenvía: renglón otra vez sin acuse

        await b.Lector.ProcesarAsync(default);

        b.Proc.Aplicados.Should().Equal("M1");
        b.Origen.AcuseDe("M1").Should().Be(new AcuseApp("M1", "DUPLICADO", "F-1", null));
    }

    [Fact]
    public async Task Error_con_folio_frena_los_siguientes_de_su_referencia_hasta_que_la_app_reprocese()
    {
        var b = Crear();
        b.Proc.Rechazar.Add("M1");
        Poner(b, "M1", "RQ-1"); Poner(b, "M2", "RQ-1"); Poner(b, "M3", "RQ-2");

        var tick = await b.Lector.ProcesarAsync(default);

        tick.Should().Be(new AlmMensajeTick(2, 1, 1, 0)); // M1 ERROR, M2 espera, M3 de otra referencia sigue
        b.Origen.AcuseDe("M1").Should().Be(new AcuseApp("M1", "ERROR", "REV-M1", "Sin existencia suficiente"));
        b.Origen.AcuseDe("M2").Should().BeNull();
        b.Origen.AcuseDe("M3")!.Estado.Should().Be("RECIBIDO");
        // Siguientes ciclos: M2 sigue esperando mientras M1 esté en ERROR (el origen ni lo ofrece).
        (await b.Lector.ProcesarAsync(default)).Should().Be(default(AlmMensajeTick));
        b.Origen.AcuseDe("M2").Should().BeNull();

        // Alguien revisa; la app reprocesa M1 (ya hay existencia): se acusa y M2 se libera.
        b.Proc.Rechazar.Clear(); b.Proc.Hechos.Remove("M1");
        b.Origen.Reprocesar("M1");
        await b.Lector.ProcesarAsync(default);
        await b.Lector.ProcesarAsync(default);
        b.Origen.AcuseDe("M1")!.Estado.Should().Be("RECIBIDO");
        b.Origen.AcuseDe("M2")!.Estado.Should().Be("RECIBIDO");
    }

    [Fact]
    public async Task Reenvio_es_renglon_nuevo_con_intento_mas_uno_y_conserva_su_lugar_en_la_referencia()
    {
        var b = Crear();
        b.Proc.Rechazar.Add("M1");
        Poner(b, "M1", "RQ-1"); Poner(b, "M2", "RQ-1");
        await b.Lector.ProcesarAsync(default); // M1 ERROR (intento 1), M2 espera

        b.Proc.Rechazar.Clear(); b.Proc.Hechos.Remove("M1");
        b.Origen.Reprocesar("M1");
        var pendientes = await b.Origen.LeerPendientesAsync(10, default);

        // El renglón viejo en ERROR ya no frena; el reenvío (intento 2) va antes que M2 aunque su id sea mayor.
        pendientes.Select(p => (p.MensajeId, p.Intento)).Should().Equal(("M1", 2), ("M2", 1));
        await b.Lector.ProcesarAsync(default);
        b.Proc.Aplicados.Should().Equal("M1", "M1", "M2"); // intento 1 (ERROR), intento 2 y recién después M2
    }

    [Fact]
    public async Task Falla_del_ERP_no_acusa_reintenta_y_tras_MaxIntentos_acusa_ERROR_sin_filtrar_el_detalle()
    {
        var b = Crear(maxIntentos: 3);
        b.Proc.Lanzar.Add("M1");
        Poner(b, "M1", "RQ-1"); Poner(b, "M2", "RQ-1"); Poner(b, "M3", "RQ-2");

        var t1 = await b.Lector.ProcesarAsync(default);
        t1.Should().Be(new AlmMensajeTick(1, 0, 1, 1)); // M3 pasa; M1 falló y M2 (misma referencia) no se toma antes
        b.Origen.AcuseDe("M1").Should().BeNull();
        b.Origen.AcuseDe("M2").Should().BeNull();

        await b.Lector.ProcesarAsync(default);
        var t3 = await b.Lector.ProcesarAsync(default);

        t3.Procesados.Should().Be(1);
        var acuse = b.Origen.AcuseDe("M1")!;
        acuse.Estado.Should().Be("ERROR");
        acuse.Error.Should().NotContain("falla del ERP"); // no se filtra el texto interno
        b.Origen.AcuseDe("M2").Should().BeNull(); // ya frenado por el ERROR de M1
    }

    [Fact]
    public async Task Falla_de_lectura_de_la_base_lanza_y_el_siguiente_ciclo_se_recupera()
    {
        var b = Crear();
        Poner(b, "M1", null);
        b.Origen.FallasDeLectura = 1;

        var act = () => b.Lector.ProcesarAsync(default);
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await b.Lector.ProcesarAsync(default)).Procesados.Should().Be(1);
    }

    [Fact]
    public async Task Lento_si_se_cancela_a_mitad_no_deja_acuse_y_el_ciclo_siguiente_lo_aplica_una_sola_vez()
    {
        var b = Crear();
        b.Proc.Espera = TimeSpan.FromSeconds(30);
        Poner(b, "M1", "RQ-1");
        using var corto = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var act = () => b.Lector.ProcesarAsync(corto.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        b.Origen.AcuseDe("M1").Should().BeNull();

        b.Proc.Espera = TimeSpan.Zero;
        await b.Lector.ProcesarAsync(default);
        b.Proc.Aplicados.Should().Equal("M1");
        b.Origen.AcuseDe("M1")!.Estado.Should().Be("RECIBIDO");
    }

    [Fact]
    public async Task Sobre_invalido_o_con_otro_mensajeId_acusa_ERROR_sin_llamar_al_manejador()
    {
        var b = Crear();
        b.Origen.Encolar("M1", "SALIDA_MANUAL", "RQ-1", "{no es json");
        b.Origen.Encolar("M2", "SALIDA_MANUAL", "RQ-2", Sobre("OTRO"));

        var tick = await b.Lector.ProcesarAsync(default);

        tick.Errores.Should().Be(2);
        b.Proc.Aplicados.Should().BeEmpty();
        b.Origen.AcuseDe("M1")!.Error.Should().Contain("JSON");
        b.Origen.AcuseDe("M2")!.Error.Should().Contain("no coincide");
    }

    [Fact]
    public async Task Un_acuse_ya_escrito_no_se_pisa()
    {
        var b = Crear();
        var sec = Poner(b, "M1", null);
        await b.Lector.ProcesarAsync(default);

        await b.Origen.EscribirAcuseAsync(sec, new AcuseApp("M1", "DUPLICADO", "X", null), default);

        b.Origen.AcuseDe("M1")!.Estado.Should().Be("RECIBIDO");
    }

    [Fact]
    public async Task Worker_apagado_por_omision_no_lee_la_tabla()
    {
        var b = Crear();
        Poner(b, "M1", null);
        var sp = new ServiceCollection().AddSingleton<IProcesadorMensajeApp>(b.Proc).BuildServiceProvider();
        var opciones = Options.Create(new AlmMensajeOptions());
        var carga = new SimuladoAlmCargaInicialOrigen();
        carga.Agregar(new AlmCargaInicialRenglon(1, 1, "CONKAL", "GENERAL", null, 300120, null, null, null, null, null, null, 1, null,
            null, 1m, "PESOSMX", null));
        var worker = new AlmMensajeWorker(
            new AlmMensajeLector(b.Origen, sp.GetRequiredService<IServiceScopeFactory>(), opciones, NullLogger<AlmMensajeLector>.Instance),
            new AlmCargaInicialLector(carga, sp.GetRequiredService<IServiceScopeFactory>(), opciones, NullLogger<AlmCargaInicialLector>.Instance),
            opciones, NullLogger<AlmMensajeWorker>.Instance);

        await worker.StartAsync(default);
        await worker.StopAsync(default);

        opciones.Value.Habilitado.Should().BeFalse();
        opciones.Value.CargaInicialHabilitada.Should().BeFalse();
        b.Origen.AcuseDe("M1").Should().BeNull();
        carga.Acuse(1, 1).Should().Be(((string?)null, (string?)null)); // M-283: la carga inicial también está apagada
    }
}
