using System.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Compartido.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.TipoCambio;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.Integraciones.Aw.Infrastructure.TipoCambio;
using Millet.Integraciones.Aw.Infrastructure.TipoCambio.Workers;
using Npgsql;
using Xunit;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>
/// Pruebas de integración sobre PostgreSQL Demo (contenedor millet-erp-postgres-test-1)
/// para el flujo 14 / I13.2 de publicación y supervisión de Tipo de Cambio (dbo.erp_tipo_cambio y sysadm.ka_waehrungen).
/// Valida CA13.9 e I13.2-a..g:
/// - CA13.9: Publicación exitosa y paridad con A+W (SYSADM.KA_WAEHRUNGEN).
/// - I13.2-a: Alerta a las 18:00 cuando falta TC de mañana.
/// - I13.2-b: Inserción de fin de semana (sábado, domingo, lunes) con el último valor hábil.
/// - I13.2-c: Idempotencia en múltiples ejecuciones (no duplica registros).
/// - I13.2-d: Corrección de valor crea versión 2.
/// - I13.2-e: Detección de acuse rechazado 3 veces por TI a las 09:00.
/// </summary>
[Collection("AwClientesSync")]
public class AwTipoCambioPgTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private string _cs = "";

    public async Task InitializeAsync()
    {
        _cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")!;
        await EjecutarAsync("DROP SCHEMA IF EXISTS dbo CASCADE; DROP SCHEMA IF EXISTS aw_origen CASCADE; DROP SCHEMA IF EXISTS sysadm CASCADE;");
        await EjecutarAsync(await File.ReadAllTextAsync(Path.Combine(AwOrigenPgTests.DirectorioOrigenDemo(), "schema.sql")));
    }

    public async Task DisposeAsync() =>
        await EjecutarAsync("DROP SCHEMA IF EXISTS dbo CASCADE; DROP SCHEMA IF EXISTS aw_origen CASCADE; DROP SCHEMA IF EXISTS sysadm CASCADE;");

    private AwTipoCambioAdapter Adapter() => new(
        new AwOrigenPg.Fabrica(_cs),
        Options.Create(new AwPedidosOptions
        {
            SqlConnectTimeoutSeconds = 5, SqlQueryTimeoutSeconds = 15,
            MapeoMonedaAw = new(StringComparer.OrdinalIgnoreCase) { ["MXN"] = "PESOSMX", ["USD"] = "USD", ["EUR"] = "Euro", ["CAD"] = "CAN" },
        }),
        AwTipoCambioAdapter.CandadoPostgres);

    private async Task EjecutarAsync(string sql, params (string, object?)[] ps)
    {
        await using var cn = new NpgsqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (n, v) in ps)
        {
            if (v is DateOnly d)
            {
                var p = cmd.Parameters.AddWithValue(n, d.ToDateTime(TimeOnly.MinValue));
                p.DbType = DbType.Date;
            }
            else
            {
                cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
            }
        }
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<(string Moneda, DateOnly Fecha, int Version, decimal Valor, byte? Resultado, int Intentos)>> FilasAsync(
        string? moneda = null)
    {
        await using var cn = new NpgsqlConnection(_cs);
        await cn.OpenAsync();
        var sql = "SELECT moneda, fecha_aplicacion, version, valor, resultado, intentos FROM dbo.erp_tipo_cambio";
        if (!string.IsNullOrEmpty(moneda))
        {
            sql += " WHERE moneda = @m";
        }
        sql += " ORDER BY moneda, fecha_aplicacion, version";

        await using var cmd = new NpgsqlCommand(sql, cn);
        if (!string.IsNullOrEmpty(moneda))
        {
            cmd.Parameters.AddWithValue("m", moneda);
        }

        var lista = new List<(string, DateOnly, int, decimal, byte?, int)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var m = r.GetString(0);
            var f = DateOnly.FromDateTime(r.GetDateTime(1));
            var v = r.GetInt32(2);
            var val = r.GetDecimal(3);
            byte? res = r.IsDBNull(4) ? null : (byte?)r.GetInt16(4);
            var intentos = r.GetInt32(5);
            lista.Add((m, f, v, val, res, intentos));
        }
        return lista;
    }

    private (AwTipoCambioWorker Worker, FakeTipoCambioRangoReadPort RangoPort, TestLogger<AwTipoCambioWorker> Logger) CrearWorker(
        AwTipoCambioOptions? options = null)
    {
        var opts = Options.Create(options ?? new AwTipoCambioOptions
        {
            Habilitado = true,
            HoraLimitePublicacion = "18:00",
            HoraLimiteAplicacion = "09:00",
            Monedas = ["USD", "EUR", "CAD"]
        });

        var rangoPort = new FakeTipoCambioRangoReadPort();
        var logger = new TestLogger<AwTipoCambioWorker>();
        var adapter = Adapter();

        var services = new ServiceCollection();
        services.AddSingleton<ITipoCambioRangoReadPort>(rangoPort);
        services.AddSingleton<IAwTipoCambioPort>(adapter);
        services.AddSingleton(opts);
        services.AddSingleton<ILogger<AwTipoCambioPublicador>>(NullLogger<AwTipoCambioPublicador>.Instance);
        services.AddScoped<AwTipoCambioPublicador>();

        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var worker = new AwTipoCambioWorker(scopeFactory, opts, logger);
        return (worker, rangoPort, logger);
    }

    // =========================================================================
    // CA13.9: Publicación exitosa y paridad con A+W (SYSADM.KA_WAEHRUNGEN)
    // =========================================================================
    [Fact]
    public async Task CA13_9_PublicacionYParidadConAw_Exitoso()
    {
        var adapter = Adapter();
        var inicio = new DateOnly(2026, 10, 15);
        var fin = new DateOnly(2026, 10, 23);

        // 1. Publicar USD, EUR y CAD para el rango del 15 al 23 de octubre
        for (var f = inicio; f <= fin; f = f.AddDays(1))
        {
            var rUsd = await adapter.PublicarFilaAsync(new AwTipoCambioFila("USD", f, 1, 19.8500m, f.AddDays(-1), "Banxico"));
            var rEur = await adapter.PublicarFilaAsync(new AwTipoCambioFila("EUR", f, 1, 21.5000m, f.AddDays(-1), "Banxico"));
            var rCad = await adapter.PublicarFilaAsync(new AwTipoCambioFila("CAD", f, 1, 14.3000m, f.AddDays(-1), "Banxico"));

            rUsd.Should().Be(1);
            rEur.Should().Be(1);
            rCad.Should().Be(1);
        }

        // 9 días * 3 monedas = 27 filas
        var filas = await FilasAsync();
        filas.Should().HaveCount(27);

        // 2. Simular tarea de TI: aplica USD de 2026-10-15
        await EjecutarAsync(
            "UPDATE dbo.erp_tipo_cambio SET resultado = 1, aplicada_at = CURRENT_TIMESTAMP WHERE moneda = 'USD' AND fecha_aplicacion = @f",
            ("f", inicio));

        // 3. Insertar paridad vigente en catálogo A+W (sysadm.ka_waehrungen)
        await EjecutarAsync(
            "INSERT INTO sysadm.ka_waehrungen (waehrung, fremd_key, bez, valutakurs, valutadatum) " +
            "VALUES ('USD', 'USD', 'Dólares Americanos', 0.05343393, CURRENT_TIMESTAMP)");

        // 4. Verificar consulta de paridad y acuses
        var tcAw = await adapter.ObtenerTipoCambioVigenteAwAsync("USD");
        tcAw.Should().Be(0.05343393m);

        var acuses = await adapter.ObtenerUltimosAcusesAsync(["USD", "EUR", "CAD"], inicio);
        var acuseUsd = acuses.First(a => a.Moneda == "USD" && a.FechaAplicacion == inicio);
        acuseUsd.Resultado.Should().Be((byte)1);
        acuseUsd.AplicadaAt.Should().NotBeNull();
        acuseUsd.Valor.Should().Be(19.8500m);
    }

    // =========================================================================
    // I13.2-a: Alerta a las 18:00 cuando falta FIX del día siguiente
    // =========================================================================
    [Fact]
    public async Task I13_2_a_FaltaTipoCambioManana_AlertaALas18()
    {
        var (worker, rangoPort, logger) = CrearWorker();
        var hoy = new DateOnly(2026, 10, 15);
        var manana = hoy.AddDays(1);

        // Proveer únicamente tipo de cambio para hoy (mañana no está disponible)
        rangoPort.Items.Add(new TipoCambioRangoItem("USD", hoy, 19.8500m, "Banxico"));
        rangoPort.Items.Add(new TipoCambioRangoItem("EUR", hoy, 21.5000m, "Banxico"));
        rangoPort.Items.Add(new TipoCambioRangoItem("CAD", hoy, 14.3000m, "Banxico"));

        // Ejecutar a las 18:00 hrs
        var ahora = new DateTimeOffset(2026, 10, 15, 18, 0, 0, TimeSpan.FromHours(-6));
        await worker.EjecutarCicloAsync(ahora);

        // Verificar que se emitió la Alerta 1 para mañana
        logger.Logs.Should().Contain(l =>
            l.Level == LogLevel.Warning
            && l.Message.Contains("ALERTA_TC_MANANA_SIN_PUBLICAR")
            && l.Message.Contains("USD")
            && l.Message.Contains(manana.ToString("yyyy-MM-dd")));

        logger.Logs.Should().Contain(l =>
            l.Level == LogLevel.Warning
            && l.Message.Contains("ALERTA_TC_MANANA_SIN_PUBLICAR")
            && l.Message.Contains("EUR")
            && l.Message.Contains(manana.ToString("yyyy-MM-dd")));

        logger.Logs.Should().Contain(l =>
            l.Level == LogLevel.Warning
            && l.Message.Contains("ALERTA_TC_MANANA_SIN_PUBLICAR")
            && l.Message.Contains("CAD")
            && l.Message.Contains(manana.ToString("yyyy-MM-dd")));
    }

    // =========================================================================
    // I13.2-b: Publicación de fin de semana (sábado, domingo, lunes) con último valor hábil
    // =========================================================================
    [Fact]
    public async Task I13_2_b_PublicacionFinDeSemana_CubreSabadoDomingoLunes()
    {
        var (worker, rangoPort, _) = CrearWorker();
        var viernes = new DateOnly(2026, 10, 16);
        var sabado = new DateOnly(2026, 10, 17);
        var domingo = new DateOnly(2026, 10, 18);
        var lunes = new DateOnly(2026, 10, 19);

        const decimal valorViernes = 19.8750m;

        // La escalera K10.6 proyecta el valor del viernes a sábado, domingo y lunes
        rangoPort.Items.Add(new TipoCambioRangoItem("USD", viernes, valorViernes, "Banxico"));
        rangoPort.Items.Add(new TipoCambioRangoItem("USD", sabado, valorViernes, "Banxico"));
        rangoPort.Items.Add(new TipoCambioRangoItem("USD", domingo, valorViernes, "Banxico"));
        rangoPort.Items.Add(new TipoCambioRangoItem("USD", lunes, valorViernes, "Banxico"));

        // Corrida del viernes a las 12:30 hrs
        var ahora = new DateTimeOffset(2026, 10, 16, 12, 30, 0, TimeSpan.FromHours(-6));
        await worker.EjecutarCicloAsync(ahora);

        // Verificar inserción de las 4 fechas en la base de datos
        var filas = await FilasAsync("USD");
        filas.Should().HaveCount(4);

        filas.Select(f => f.Fecha).Should().Equal(viernes, sabado, domingo, lunes);
        filas.Should().OnlyContain(f => f.Version == 1 && f.Valor == valorViernes);
    }

    // =========================================================================
    // I13.2-c: Idempotencia en múltiples ejecuciones (no duplica registros)
    // =========================================================================
    [Fact]
    public async Task I13_2_c_Idempotencia_MultiplesEjecucionesNoDuplican()
    {
        var adapter = Adapter();
        var fecha = new DateOnly(2026, 10, 20);
        var fila = new AwTipoCambioFila("USD", fecha, 1, 19.8500m, fecha.AddDays(-1), "Banxico");

        // Tres llamadas consecutivas con el mismo valor
        var v1 = await adapter.PublicarFilaAsync(fila);
        var v2 = await adapter.PublicarFilaAsync(fila);
        var v3 = await adapter.PublicarFilaAsync(fila);

        v1.Should().Be(1);
        v2.Should().BeNull();
        v3.Should().BeNull();

        // En la base de datos solo debe existir exactamente 1 registro
        var filas = await FilasAsync("USD");
        filas.Should().ContainSingle();
        filas[0].Version.Should().Be(1);
        filas[0].Valor.Should().Be(19.8500m);
    }

    // =========================================================================
    // I13.2-d: Corrección de valor crea versión 2
    // =========================================================================
    [Fact]
    public async Task I13_2_d_CorreccionValor_GeneraVersionDos()
    {
        var adapter = Adapter();
        var fecha = new DateOnly(2026, 10, 21);

        // 1. Versión inicial 19.5000
        var filaV1 = new AwTipoCambioFila("USD", fecha, 1, 19.5000m, null, "Manual");
        var resV1 = await adapter.PublicarFilaAsync(filaV1);
        resV1.Should().Be(1);

        // 2. Corrección posterior con 19.8000
        var filaV2 = new AwTipoCambioFila("USD", fecha, 1, 19.8000m, null, "Manual");
        var resV2 = await adapter.PublicarFilaAsync(filaV2);
        resV2.Should().Be(2);

        // 3. En la base de datos deben existir 2 versiones ordenadas
        var filas = await FilasAsync("USD");
        filas.Should().HaveCount(2);

        filas[0].Version.Should().Be(1);
        filas[0].Valor.Should().Be(19.5000m);

        filas[1].Version.Should().Be(2);
        filas[1].Valor.Should().Be(19.8000m);

        // 4. El acuse de barrido reporta la versión 2 (la más reciente)
        var acuses = await adapter.ObtenerUltimosAcusesAsync(["USD"], fecha);
        var acuse = acuses.Should().ContainSingle().Subject;
        acuse.Version.Should().Be(2);
        acuse.Valor.Should().Be(19.8000m);
    }

    // =========================================================================
    // I13.2-e: Detección de acuse rechazado 3 veces por TI a las 09:00
    // =========================================================================
    [Fact]
    public async Task I13_2_e_RechazosTI_AlertaConTresIntentos()
    {
        var adapter = Adapter();
        var hoy = new DateOnly(2026, 10, 22);

        // 1. Publicar fila para hoy
        await adapter.PublicarFilaAsync(new AwTipoCambioFila("USD", hoy, 1, 19.8500m, hoy.AddDays(-1), "Banxico"));

        // 2. TI actualiza la fila reportando 3 rechazos consecutivos
        await EjecutarAsync(
            "UPDATE dbo.erp_tipo_cambio SET resultado = 2, intentos = 3, motivo = 'A+W bloqueado: error de conexión' " +
            "WHERE moneda = 'USD' AND fecha_aplicacion = @f",
            ("f", hoy));

        // 3. Worker ejecuta ciclo de las 09:00 hrs
        var (worker, _, logger) = CrearWorker();
        var ahora = new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.FromHours(-6));
        await worker.EjecutarCicloAsync(ahora);

        // 4. Verificar que se emite la Alerta 2 (ALERTA_TC_NO_APLICADO_AW)
        logger.Logs.Should().Contain(l =>
            l.Level == LogLevel.Warning
            && l.Message.Contains("ALERTA_TC_NO_APLICADO_AW")
            && l.Message.Contains("USD")
            && l.Message.Contains("Resultado: 2")
            && l.Message.Contains("Intentos: 3"));
    }

    // =========================================================================
    // CA13.9: Alerta de discrepancia ERP vs A+W cuando los valores difieren
    // =========================================================================
    [Fact]
    public async Task CA13_9_AlertaDesfaseErpVsAw_CuandoValoresNoCoinciden()
    {
        var adapter = Adapter();
        var hoy = new DateOnly(2026, 10, 23);

        // 1. Publicar valor oficial del ERP (19.8500)
        await adapter.PublicarFilaAsync(new AwTipoCambioFila("USD", hoy, 1, 19.8500m, hoy.AddDays(-1), "Banxico"));

        // 2. TI marcó como aplicada (resultado = 1)
        await EjecutarAsync(
            "UPDATE dbo.erp_tipo_cambio SET resultado = 1, aplicada_at = CURRENT_TIMESTAMP WHERE moneda = 'USD' AND fecha_aplicacion = @f",
            ("f", hoy));

        // 3. Pero en A+W ka_waehrungen quedó un valor desfasado (19.2000)
        await EjecutarAsync(
            "INSERT INTO sysadm.ka_waehrungen (waehrung, fremd_key, bez, valutakurs, valutadatum) " +
            "VALUES ('USD', 'USD', 'USD', 19.2000, CURRENT_TIMESTAMP)");

        // 4. Worker corre a las 09:15 hrs
        var (worker, _, logger) = CrearWorker();
        var ahora = new DateTimeOffset(2026, 10, 23, 9, 15, 0, TimeSpan.FromHours(-6));
        await worker.EjecutarCicloAsync(ahora);

        // 5. Verificar emisión de Alerta 3 (ALERTA_TC_DESFASE_ERP_AW)
        logger.Logs.Should().Contain(l =>
            l.Level == LogLevel.Warning
            && l.Message.Contains("ALERTA_TC_DESFASE_ERP_AW")
            && l.Message.Contains("USD")
            && l.Message.Contains("19.8500")
            && l.Message.Contains("19.2000"));
    }

    // =========================================================================
    // Concurrencia: llamadas simultáneas serializadas por CandadoPostgres
    // =========================================================================
    [Fact]
    public async Task Concurrencia_PublicacionSimultanea_NoDuplicaYCandadoPostgresSerializa()
    {
        var fecha = new DateOnly(2026, 10, 24);
        var fila = new AwTipoCambioFila("USD", fecha, 1, 19.8500m, null, "Manual");

        // 8 hilos simultáneos intentando publicar exactamente la misma fila
        var tareas = Enumerable.Range(0, 8).Select(_ => Task.Run(() => Adapter().PublicarFilaAsync(fila)));
        var resultados = await Task.WhenAll(tareas);

        // Exactamente 1 debe haber insertado (versión 1) y los 7 restantes omitidos (null)
        resultados.Count(r => r == 1).Should().Be(1);
        resultados.Count(r => r is null).Should().Be(7);

        var filas = await FilasAsync("USD");
        filas.Should().ContainSingle();
    }

    // =========================================================================
    // Restricciones DDL de schema.sql en la tabla puente dbo.erp_tipo_cambio
    // =========================================================================
    [Fact]
    public async Task RestriccionesDdl_TablaPuente_ValidanEsquemaDemo()
    {
        // 1. Moneda no permitida ('GBP')
        var sqlMonedaInvalida = "INSERT INTO dbo.erp_tipo_cambio (moneda, fecha_aplicacion, version, valor, origen) VALUES ('GBP', CURRENT_DATE, 1, 20.0, 'Manual')";
        await FluentActions.Awaiting(() => EjecutarAsync(sqlMonedaInvalida)).Should().ThrowAsync<PostgresException>();

        // 2. Versión <= 0
        var sqlVersionCero = "INSERT INTO dbo.erp_tipo_cambio (moneda, fecha_aplicacion, version, valor, origen) VALUES ('USD', CURRENT_DATE, 0, 20.0, 'Manual')";
        await FluentActions.Awaiting(() => EjecutarAsync(sqlVersionCero)).Should().ThrowAsync<PostgresException>();

        // 3. Valor <= 0
        var sqlValorCero = "INSERT INTO dbo.erp_tipo_cambio (moneda, fecha_aplicacion, version, valor, origen) VALUES ('USD', CURRENT_DATE, 1, 0.0, 'Manual')";
        await FluentActions.Awaiting(() => EjecutarAsync(sqlValorCero)).Should().ThrowAsync<PostgresException>();

        // 4. Origen inválido ('Otro')
        var sqlOrigenInvalido = "INSERT INTO dbo.erp_tipo_cambio (moneda, fecha_aplicacion, version, valor, origen) VALUES ('USD', CURRENT_DATE, 1, 20.0, 'Otro')";
        await FluentActions.Awaiting(() => EjecutarAsync(sqlOrigenInvalido)).Should().ThrowAsync<PostgresException>();

        // 5. Resultado inválido (4)
        var sqlResultadoInvalido = "INSERT INTO dbo.erp_tipo_cambio (moneda, fecha_aplicacion, version, valor, origen, resultado) VALUES ('USD', CURRENT_DATE, 1, 20.0, 'Manual', 4)";
        await FluentActions.Awaiting(() => EjecutarAsync(sqlResultadoInvalido)).Should().ThrowAsync<PostgresException>();

        // 6. Intentos negativos (-1)
        var sqlIntentosNegativos = "INSERT INTO dbo.erp_tipo_cambio (moneda, fecha_aplicacion, version, valor, origen, intentos) VALUES ('USD', CURRENT_DATE, 1, 20.0, 'Manual', -1)";
        await FluentActions.Awaiting(() => EjecutarAsync(sqlIntentosNegativos)).Should().ThrowAsync<PostgresException>();
    }

    // =========================================================================
    // Test Doubles de Soporte
    // =========================================================================
    private sealed class FakeTipoCambioRangoReadPort : ITipoCambioRangoReadPort
    {
        public List<TipoCambioRangoItem> Items { get; } = [];

        public Task<IReadOnlyList<TipoCambioRangoItem>> ObtenerRangoTipoCambioAsync(
            IReadOnlyList<string> monedas, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
        {
            var res = Items
                .Where(x => monedas.Contains(x.Moneda, StringComparer.OrdinalIgnoreCase) && x.Fecha >= desde && x.Fecha <= hasta)
                .ToList();
            return Task.FromResult<IReadOnlyList<TipoCambioRangoItem>>(res);
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Logs { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Logs.Add((logLevel, formatter(state, exception)));
        }
    }
}
