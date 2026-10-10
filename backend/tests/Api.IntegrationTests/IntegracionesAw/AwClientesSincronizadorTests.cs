using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Application.Workers;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>
/// ADM-06 Entrega C3: <see cref="AwClientesSincronizador"/> contra PostgreSQL real con un origen
/// simulado propio por prueba. Datos 100 % sintéticos; cada prueba usa un rango de IDs propio y
/// limpia barridos vivos previos (el índice único es por origen, compartido entre pruebas).
/// </summary>
[Collection("AwClientesSync")]
public class AwClientesSincronizadorTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static int _siguienteBase = Random.Shared.Next(1_000_000, 900_000_000);
    private readonly WebApplicationFactory<Program> _factory;

    public AwClientesSincronizadorTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static int NuevaBase() => Interlocked.Add(ref _siguienteBase, 10_000);

    private static AwClienteOrigenFila Fila(int id, string? name1 = null, string zahlbed = "ZDEMO", int dias = 30, int? bruttotage = null) => new(
        id, 1, name1 ?? $"CLIENTE DEMO {id}", null, null, "CALLE DEMO 1", "CIUDAD DEMO", "06600", "PROV", "MX",
        null, null, "5550001", null, null, zahlbed, "MXN", 1000m, 500m, 30d, null, 0, null, null,
        [new AwCondicionCoincidencia(30, bruttotage ?? dias)]);

    private static List<AwClienteOrigenFila> Filas(int baseId, int n) =>
        Enumerable.Range(1, n).Select(i => Fila(baseId + i)).ToList();

    private sealed class OrigenFake(List<AwClienteOrigenFila> filas) : IAwClientesOrigen
    {
        public List<AwClienteOrigenFila> Filas { get; } = filas;
        public Exception? ErrorEnLectura { get; set; }
        public Action<int>? AlLeerPagina { get; set; }
        public int Paginas { get; private set; }

        public Task<AwClienteOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct)
        {
            if (ErrorEnLectura is not null) throw ErrorEnLectura;
            return Task.FromResult(Filas.FirstOrDefault(f => f.Id.ToString() == referencia));
        }

        public Task<AwClientesPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
        {
            Paginas++;
            AlLeerPagina?.Invoke(Paginas);
            if (ErrorEnLectura is not null) throw ErrorEnLectura;
            var desde = cursor is null ? 0 : int.Parse(cursor);
            var pag = Filas.Where(f => f.Id > desde).OrderBy(f => f.Id).Take(tamano).ToList();
            return Task.FromResult(new AwClientesPagina(pag, pag.Count == tamano ? pag[^1].Id.ToString() : null));
        }
    }

    private static AwClientesOptions Opts(int lote = 3, bool lectura = true, bool aplicacion = true,
        AwClientesOrigenTipo origen = AwClientesOrigenTipo.Simulado) => new()
    {
        Origen = origen, LecturaHabilitada = lectura, AplicacionHabilitada = aplicacion, TamanoLote = lote,
        MapeoMoneda = new(StringComparer.OrdinalIgnoreCase) { ["MXN"] = "MXN" }, // sin mapeo la fila queda Pendiente
    };

    private async Task<T> ConSync<T>(OrigenFake? origen, AwClientesOptions opts, Func<AwClientesSincronizador, IntegracionesAwDbContext, CompartidoDbContext, Task<T>> f)
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = sp.GetRequiredService<IntegracionesAwDbContext>();
        var cdb = sp.GetRequiredService<CompartidoDbContext>();
        var services = new ServiceCollection();
        if (origen is not null) services.AddSingleton<IAwClientesOrigen>(origen);
        var sync = new AwClientesSincronizador(db, cdb, sp.GetRequiredService<AplicarClienteAwService>(),
            services.BuildServiceProvider(), Options.Create(opts), TimeProvider.System,
            NullLogger<AwClientesSincronizador>.Instance);
        return await f(sync, db, cdb);
    }

    private async Task LimpiarVivosAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>();
        await db.ClientesEjecuciones
            .Where(e => e.Estado == AwClientesEjecucionEstado.Pendiente || e.Estado == AwClientesEjecucionEstado.EnCurso)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Estado, AwClientesEjecucionEstado.Cancelada));
    }

    private async Task<Guid> Barrido(OrigenFake origen, AwClientesOptions opts, CancellationToken ct = default)
    {
        await LimpiarVivosAsync();
        return await ConSync<Guid>(origen, opts, async (s, _, _) =>
        {
            var id = await s.IniciarBarridoAsync("test", CancellationToken.None);
            await s.EjecutarAsync(id, ct);
            return id;
        });
    }

    private async Task<AwClientesEjecucion> Ejec(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones
            .AsNoTracking().Include(e => e.ErroresPorReferencia).SingleAsync(e => e.Id == id);
    }

    private async Task<(List<Cliente> Clientes, List<ClienteSincronizacionAw> Regs)> Leer(IEnumerable<int> ids)
    {
        var refs = ids.Select(i => i.ToString()).ToList();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return (await db.Clientes.AsNoTracking().Where(c => c.ReferenciaExterna != null && refs.Contains(c.ReferenciaExterna)).ToListAsync(),
            await db.Set<ClienteSincronizacionAw>().AsNoTracking().Where(r => refs.Contains(r.ReferenciaExterna)).ToListAsync());
    }

    private static IEnumerable<int> Ids(List<AwClienteOrigenFila> f) => f.Select(x => x.Id);

    [Fact]
    public async Task Barrido_feliz_multiples_lotes_completa_con_contadores_y_sin_duplicados()
    {
        var filas = Filas(NuevaBase(), 8);
        var origen = new OrigenFake(filas);
        var e = await Ejec(await Barrido(origen, Opts(lote: 3)));

        Assert.Equal(AwClientesEjecucionEstado.Completa, e.Estado);
        Assert.Equal((8, 8, 0, 0, 0, 0), (e.Leidos, e.Creados, e.Actualizados, e.SinCambios, e.Pendientes, e.Errores));
        Assert.True(origen.Paginas >= 3);
        var (cs, regs) = await Leer(Ids(filas));
        Assert.Equal(8, cs.Count);
        Assert.Equal(8, regs.Count);
        Assert.All(cs, c => Assert.Equal(OrigenMaster.Aw, c.Origen));
    }

    [Fact]
    public async Task Fila_con_KZ_STATUS_sin_mapeo_cuenta_como_Creado_y_ademas_Pendiente()
    {
        var filas = new List<AwClienteOrigenFila> { Fila(NuevaBase() + 1) with { KzStatus = 1 } };
        var e = await Ejec(await Barrido(new OrigenFake(filas), Opts()));

        Assert.Equal((1, 1, 1), (e.Leidos, e.Creados, e.Pendientes));
        Assert.Single((await Leer(Ids(filas))).Clientes);
    }

    [Fact]
    public async Task Fila_aplicada_normal_cuenta_Creado_sin_Pendiente()
    {
        var filas = new List<AwClienteOrigenFila> { Fila(NuevaBase() + 1) };
        var e = await Ejec(await Barrido(new OrigenFake(filas), Opts()));

        Assert.Equal((1, 1, 0), (e.Leidos, e.Creados, e.Pendientes));
    }

    [Fact]
    public async Task Repeticion_del_barrido_es_SinCambios_y_no_duplica()
    {
        var filas = Filas(NuevaBase(), 7);
        var origen = new OrigenFake(filas);
        await Barrido(origen, Opts());
        var e = await Ejec(await Barrido(origen, Opts()));

        Assert.Equal(AwClientesEjecucionEstado.Completa, e.Estado);
        Assert.Equal((7, 0, 0, 7), (e.Leidos, e.Creados, e.Actualizados, e.SinCambios));
        var (cs, regs) = await Leer(Ids(filas));
        Assert.Equal(7, cs.Count);
        Assert.Equal(7, regs.Count);
    }

    [Fact]
    public async Task Catalogo_cambiado_misma_ZAHLBED_otros_dias_actualiza_el_registro()
    {
        var b = NuevaBase();
        var origen = new OrigenFake([Fila(b + 1, zahlbed: "ZCAT", dias: 30)]);
        await Barrido(origen, Opts());
        origen.Filas[0] = Fila(b + 1, zahlbed: "ZCAT", dias: 30, bruttotage: 60);
        var e = await Ejec(await Barrido(origen, Opts()));

        Assert.Equal(1, e.Actualizados);
        var (_, regs) = await Leer([b + 1]);
        Assert.Equal(60, Assert.Single(regs).DiasNominalesOrigen);
    }

    [Fact]
    public async Task ReintentarReferencia_relee_y_aplica_el_valor_vigente_del_origen()
    {
        var b = NuevaBase();
        var origen = new OrigenFake([Fila(b + 1, name1: "CLIENTE DEMO V1")]);
        await Barrido(origen, Opts());
        origen.Filas[0] = Fila(b + 1, name1: "CLIENTE DEMO V1") with { Strasse = "CALLE VIGENTE 77" };

        var id = await ConSync(origen, Opts(), (s, _, _) => s.ReintentarReferenciaAsync((b + 1).ToString(), "test", CancellationToken.None));
        var e = await Ejec(id);

        Assert.Equal(AwClientesEjecucionTipo.Referencia, e.Tipo);
        Assert.Equal(AwClientesEjecucionEstado.Completa, e.Estado);
        Assert.Equal(1, e.Actualizados);
        var (_, regs) = await Leer([b + 1]);
        Assert.Equal("CALLE VIGENTE 77", Assert.Single(regs).DomicilioOrigenCalle);
    }

    [Fact]
    public async Task ReintentarReferencia_ausente_registra_error_y_no_da_de_baja()
    {
        var b = NuevaBase();
        var origen = new OrigenFake([Fila(b + 1)]);
        await Barrido(origen, Opts());
        origen.Filas.Clear();

        var id = await ConSync(origen, Opts(), (s, _, _) => s.ReintentarReferenciaAsync((b + 1).ToString(), "test", CancellationToken.None));
        var e = await Ejec(id);

        Assert.Equal(AwClientesEjecucionEstado.Parcial, e.Estado);
        Assert.Equal("no_encontrada_en_origen", Assert.Single(e.ErroresPorReferencia).Codigo);
        var (cs, _) = await Leer([b + 1]);
        Assert.Equal(EstatusCatalogo.Activo, Assert.Single(cs).Estatus);
    }

    [Fact]
    public async Task Cancelar_a_mitad_deja_EnCurso_con_cursor_y_Reanudar_completa_sin_duplicar()
    {
        var filas = Filas(NuevaBase(), 8);
        var origen = new OrigenFake(filas);
        using var cts = new CancellationTokenSource();
        origen.AlLeerPagina = n => { if (n == 2) cts.Cancel(); };

        await LimpiarVivosAsync();
        var id = await ConSync<Guid>(origen, Opts(lote: 3), async (s, _, _) =>
        {
            var i = await s.IniciarBarridoAsync("test", CancellationToken.None);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.EjecutarAsync(i, cts.Token));
            return i;
        });

        var e1 = await Ejec(id);
        Assert.Equal(AwClientesEjecucionEstado.EnCurso, e1.Estado);
        Assert.Equal((filas[0].Id + 2).ToString(), e1.CursorActual); // último ID del lote 1
        Assert.Equal(3, e1.Leidos);

        origen.AlLeerPagina = null;
        await ConSync(origen, Opts(lote: 3), (s, _, _) => s.ReanudarAsync(id, CancellationToken.None).ContinueWith(_ => 0));
        var e2 = await Ejec(id);
        Assert.Equal(AwClientesEjecucionEstado.Completa, e2.Estado);
        Assert.Equal((8, 8), (e2.Leidos, e2.Creados));
        var (cs, regs) = await Leer(Ids(filas));
        Assert.Equal(8, cs.Count);
        Assert.Equal(8, regs.Count);
    }

    [Fact]
    public async Task Dos_barridos_simultaneos_el_segundo_se_rechaza()
    {
        await LimpiarVivosAsync();
        var origen = new OrigenFake(Filas(NuevaBase(), 2));
        var tareas = Enumerable.Range(0, 2).Select(_ => ConSync<Guid?>(origen, Opts(), async (s, _, _) =>
        {
            try { return await s.IniciarBarridoAsync("test", CancellationToken.None); }
            catch (AwClientesSyncException ex) { Assert.Equal("barrido_en_curso", ex.Code); return null; }
        })).ToList();
        var res = await Task.WhenAll(tareas);

        Assert.Equal(1, res.Count(r => r is not null));
        await LimpiarVivosAsync();
    }

    [Fact]
    public async Task Fallo_parcial_fila_invalida_y_excepcion_de_aplicacion_terminan_Parcial()
    {
        var b = NuevaBase();
        var filas = new List<AwClienteOrigenFila>
        {
            Fila(b + 1), Fila(b + 2, name1: "  "), Fila(b + 3, name1: new string('X', 300)), Fila(b + 4),
        };
        var e = await Ejec(await Barrido(new OrigenFake(filas), Opts()));

        Assert.Equal(AwClientesEjecucionEstado.Parcial, e.Estado);
        Assert.Equal((4, 2, 2), (e.Leidos, e.Creados, e.Errores));
        var errs = e.ErroresPorReferencia.ToDictionary(x => x.Referencia);
        Assert.Equal("fila_invalida", errs[(b + 2).ToString()].Codigo);
        Assert.Equal("aplicacion_fallida", errs[(b + 3).ToString()].Codigo);
        Assert.All(errs.Values, x => Assert.True(x.Mensaje.Length <= AwClientesEjecucion.MensajeErrorMaxLength));
        var (cs, _) = await Leer(Ids(filas));
        Assert.Equal(new[] { b + 1, b + 4 }.Select(i => i.ToString()).Order(), cs.Select(c => c.ReferenciaExterna!).Order());
    }

    [Fact]
    public async Task Error_de_lectura_del_origen_deja_Fallida_y_conserva_lo_aplicado()
    {
        var filas = Filas(NuevaBase(), 6);
        var origen = new OrigenFake(filas);
        origen.AlLeerPagina = n => { if (n == 2) origen.ErrorEnLectura = new AwReaderException(new string('e', 900), "timeout", true); };
        var e = await Ejec(await Barrido(origen, Opts(lote: 3)));

        Assert.Equal(AwClientesEjecucionEstado.Fallida, e.Estado);
        Assert.NotNull(e.ErrorGeneral);
        Assert.Equal("lectura_origen_fallida (AwReaderException)", e.ErrorGeneral); // sin ex.Message
        Assert.Equal(3, e.Creados);
        var (cs, _) = await Leer(Ids(filas));
        Assert.Equal(3, cs.Count);
    }

    [Fact]
    public async Task Ausencia_en_el_origen_no_es_baja_y_fiscales_locales_quedan_intactos()
    {
        var b = NuevaBase();
        var origen = new OrigenFake([Fila(b + 1), Fila(b + 2)]);
        await Barrido(origen, Opts());
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var c1 = await db.Clientes.SingleAsync(c => c.ReferenciaExterna == (b + 1).ToString());
            c1.ActualizarDatos(rfc: "AAA010101AAA", regimenFiscal: "601", codigoPostalFiscal: "44100");
            (await db.Clientes.SingleAsync(c => c.ReferenciaExterna == (b + 2).ToString())).CambiarEstatus(EstatusCatalogo.Inactivo);
            await db.SaveChangesAsync();
        }
        origen.Filas.RemoveAt(1); // b+2 ya no viene
        origen.Filas[0] = Fila(b + 1) with { Strasse = "OTRA CALLE" };
        var e = await Ejec(await Barrido(origen, Opts()));

        Assert.Equal(AwClientesEjecucionEstado.Completa, e.Estado);
        var (cs, _) = await Leer([b + 1, b + 2]);
        var c1r = cs.Single(c => c.ReferenciaExterna == (b + 1).ToString());
        Assert.Equal(("AAA010101AAA", "601", "44100"), (c1r.Rfc, c1r.RegimenFiscal, c1r.CodigoPostalFiscal));
        Assert.Equal(EstatusCatalogo.Inactivo, cs.Single(c => c.ReferenciaExterna == (b + 2).ToString()).Estatus);
    }

    [Theory]
    [InlineData(false, true, "lectura_deshabilitada")]
    [InlineData(true, false, "aplicacion_deshabilitada")]
    public async Task Flags_apagados_rechazan_sin_escrituras(bool lectura, bool aplicacion, string codigo)
    {
        await LimpiarVivosAsync();
        var filas = Filas(NuevaBase(), 2);
        var origen = new OrigenFake(filas);
        var opts = Opts(lectura: lectura, aplicacion: aplicacion);
        int antes;
        using (var s0 = _factory.Services.CreateScope())
            antes = await s0.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones.CountAsync();

        await ConSync<int>(origen, opts, async (s, _, _) =>
        {
            Assert.Equal(codigo, (await Assert.ThrowsAsync<AwClientesSyncException>(() => s.IniciarBarridoAsync("t", default))).Code);
            Assert.Equal(codigo, (await Assert.ThrowsAsync<AwClientesSyncException>(() => s.EjecutarAsync(Guid.NewGuid(), default))).Code);
            Assert.Equal(codigo, (await Assert.ThrowsAsync<AwClientesSyncException>(() => s.ReintentarReferenciaAsync("1", "t", default))).Code);
            return 0;
        });

        using var s1 = _factory.Services.CreateScope();
        Assert.Equal(antes, await s1.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones.CountAsync());
        Assert.Empty((await Leer(Ids(filas))).Clientes);
        Assert.Equal(0, origen.Paginas);
    }

    [Fact]
    public async Task Origen_Sql_sin_connection_string_da_origen_sin_configurar()
    {
        await ConSync<int>(null, Opts(origen: AwClientesOrigenTipo.Sql), async (s, _, _) =>
        {
            var ex = await Assert.ThrowsAsync<AwClientesSyncException>(() => s.ReintentarReferenciaAsync("1", "t", default));
            Assert.Equal("origen_sin_configurar", ex.Code);
            return 0;
        });
    }

    [Fact]
    public async Task DI_real_Origen_Sql_sin_connection_string_falla_al_usar_y_explica_la_ejecucion()
    {
        await using var f = _factory.WithWebHostBuilder(b => {
            // UseSetting (no ConfigureAppConfiguration): Program lee la sección al construir el builder.
            b.UseSetting("IntegracionesAw:Clientes:Origen", "Sql");
            b.UseSetting("IntegracionesAw:Clientes:LecturaHabilitada", "true");
            b.UseSetting("IntegracionesAw:Clientes:AplicacionHabilitada", "true");
            b.UseSetting("ConnectionStrings:AwClientesDb", "");
        });
        // Este caso ejecuta el barrido directamente: evita una carrera con el worker del host de prueba.
        await f.Services.GetRequiredService<AwClientesEjecucionDispatcher>().StopAsync(default);
        using var scope = f.Services.CreateScope();
        var origen = Assert.IsType<AwOrigenSelectores>(scope.ServiceProvider.GetRequiredService<IAwClientesOrigen>());
        var ex = await Assert.ThrowsAsync<AwClientesSyncException>(() =>
            origen.LeerPorReferenciaAsync("1", default));
        Assert.Equal("origen_sin_configurar", ex.Code);
        Assert.Equal("Origen 'Sql' sin adaptador: falta ConnectionStrings:AwClientesDb.", ex.Message);

        var sync = scope.ServiceProvider.GetRequiredService<AwClientesSincronizador>();
        var db = scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>();
        var id = await sync.ReintentarReferenciaAsync("1", "t", default);
        var ejecucion = await db.ClientesEjecuciones.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal(AwClientesEjecucionEstado.Fallida, ejecucion.Estado);
        Assert.Equal("Sql", ejecucion.Origen);
        Assert.Equal("origen_sin_configurar: " + ex.Message, ejecucion.ErrorGeneral);
        Assert.Equal(0, ejecucion.Leidos);

        var barridoId = await sync.IniciarBarridoAsync("t", default);
        await sync.EjecutarAsync(barridoId, default);
        var barrido = await db.ClientesEjecuciones.AsNoTracking().SingleAsync(e => e.Id == barridoId);
        Assert.Equal(AwClientesEjecucionEstado.Fallida, barrido.Estado);
        Assert.Equal("origen_sin_configurar: " + ex.Message, barrido.ErrorGeneral);
        Assert.Equal(0, barrido.Leidos);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void Worker_solo_se_registra_con_ProgramacionHabilitada(string valor, bool esperado)
    {
        using var f = _factory.WithWebHostBuilder(b => 
            b.UseSetting("IntegracionesAw:Clientes:ProgramacionHabilitada", valor));
        var hay = f.Services.GetServices<IHostedService>().OfType<AwClientesSyncWorker>().Any();
        Assert.Equal(esperado, hay);
    }
}
