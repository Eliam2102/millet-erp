using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Compartido.Domain.Ports;
using Millet.Integraciones.Aw.Application.TipoCambio;
using Millet.Integraciones.Aw.Infrastructure.TipoCambio.Workers;
using Xunit;

namespace Millet.Integraciones.Aw.UnitTests.TipoCambio;

public class AwTipoCambioWorkerTests
{
    private readonly FakeTipoCambioRangoReadPort _rangoReadPort = new();
    private readonly FakeAwTipoCambioPort _awPort = new();
    private readonly TestLogger<AwTipoCambioWorker> _logger = new();

    private AwTipoCambioWorker CrearWorker(AwTipoCambioOptions? options = null, TimeProvider? timeProvider = null)
    {
        var opts = Options.Create(options ?? new AwTipoCambioOptions
        {
            Habilitado = true,
            HoraLimitePublicacion = "18:00",
            HoraLimiteAplicacion = "09:00",
            Monedas = ["USD", "EUR", "CAD"]
        });

        var services = new ServiceCollection();
        services.AddSingleton<ITipoCambioRangoReadPort>(_rangoReadPort);
        services.AddSingleton<IAwTipoCambioPort>(_awPort);
        services.AddSingleton(opts);
        services.AddSingleton<ILogger<AwTipoCambioPublicador>>(NullLogger<AwTipoCambioPublicador>.Instance);
        services.AddScoped<AwTipoCambioPublicador>();

        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        return new AwTipoCambioWorker(scopeFactory, opts, _logger, timeProvider);
    }

    [Fact]
    public async Task EjecutarCicloAsync_CanalDeshabilitado_NoPublicaNiEmiteAlertas()
    {
        // Arrange
        var worker = CrearWorker(new AwTipoCambioOptions { Habilitado = false });
        var ahora = new DateTimeOffset(2026, 11, 8, 18, 30, 0, TimeSpan.FromHours(-6));

        // Act
        await worker.EjecutarCicloAsync(ahora);

        // Assert
        _awPort.FilasPublicadas.Should().BeEmpty();
        _logger.Logs.Should().BeEmpty();
    }

    [Fact]
    public async Task EjecutarCicloAsync_A_Las_18_Horas_Sin_TipoCambio_De_Manana_Emite_Alerta_1()
    {
        // Arrange (18:00, falta TC para mañana 2026-11-09)
        var worker = CrearWorker();
        var ahora = new DateTimeOffset(2026, 11, 8, 18, 0, 0, TimeSpan.FromHours(-6));

        // Act
        await worker.EjecutarCicloAsync(ahora);

        // Assert: Alerta 1 emitida para USD, EUR y CAD
        _logger.Logs.Should().Contain(l =>
            l.Level == LogLevel.Warning
            && l.Message.Contains("ALERTA_TC_MANANA_SIN_PUBLICAR")
            && l.Message.Contains("USD")
            && l.Message.Contains("2026-11-09"));

        _logger.Logs.Should().Contain(l =>
            l.Level == LogLevel.Warning
            && l.Message.Contains("ALERTA_TC_MANANA_SIN_PUBLICAR")
            && l.Message.Contains("EUR")
            && l.Message.Contains("2026-11-09"));

        _logger.Logs.Should().Contain(l =>
            l.Level == LogLevel.Warning
            && l.Message.Contains("ALERTA_TC_MANANA_SIN_PUBLICAR")
            && l.Message.Contains("CAD")
            && l.Message.Contains("2026-11-09"));
    }

    [Fact]
    public async Task EjecutarCicloAsync_A_Las_18_Horas_Con_TipoCambio_De_Manana_No_Emite_Alerta_1()
    {
        // Arrange: mañana ya tiene acuse en A+W
        var worker = CrearWorker();
        var ahora = new DateTimeOffset(2026, 11, 8, 18, 0, 0, TimeSpan.FromHours(-6));
        var manana = new DateOnly(2026, 11, 9);

        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse("USD", manana, 1, 20.50m, null, null, null, 0));
        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse("EUR", manana, 1, 22.10m, null, null, null, 0));
        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse("CAD", manana, 1, 15.30m, null, null, null, 0));

        // Act
        await worker.EjecutarCicloAsync(ahora);

        // Assert: no debe emitir Alerta 1
        _logger.Logs.Should().NotContain(l => l.Message.Contains("ALERTA_TC_MANANA_SIN_PUBLICAR"));
    }

    [Fact]
    public async Task EjecutarCicloAsync_A_Las_09_Horas_Cuando_Aw_No_Ha_Aplicado_O_Rechazado_Emite_Alerta_2()
    {
        // Arrange (09:00: USD resultado=null (no aplicado), EUR resultado=2 e intentos=3)
        var worker = CrearWorker(new AwTipoCambioOptions
        {
            Habilitado = true,
            HoraLimitePublicacion = "18:00",
            HoraLimiteAplicacion = "09:00",
            Monedas = ["USD", "EUR"]
        });

        var ahora = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.FromHours(-6));
        var hoy = new DateOnly(2026, 11, 8);

        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse("USD", hoy, 1, 20.50m, null, null, null, 0));
        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse("EUR", hoy, 1, 22.00m, null, 2, "Error de TI", 3));

        // Act
        await worker.EjecutarCicloAsync(ahora);

        // Assert: Alerta 2 emitida para USD y EUR
        _logger.Logs.Should().Contain(l =>
            l.Level == LogLevel.Warning
            && l.Message.Contains("ALERTA_TC_NO_APLICADO_AW")
            && l.Message.Contains("USD")
            && l.Message.Contains("2026-11-08"));

        _logger.Logs.Should().Contain(l =>
            l.Level == LogLevel.Warning
            && l.Message.Contains("ALERTA_TC_NO_APLICADO_AW")
            && l.Message.Contains("EUR")
            && l.Message.Contains("Resultado: 2")
            && l.Message.Contains("Intentos: 3"));
    }

    [Fact]
    public async Task EjecutarCicloAsync_A_Las_09_Horas_Cuando_Aw_Ya_Aplico_No_Emite_Alerta_2()
    {
        // Arrange: USD resultado=1 (aplicado) y coincidente
        var worker = CrearWorker(new AwTipoCambioOptions
        {
            Habilitado = true,
            HoraLimitePublicacion = "18:00",
            HoraLimiteAplicacion = "09:00",
            Monedas = ["USD"]
        });

        var ahora = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.FromHours(-6));
        var hoy = new DateOnly(2026, 11, 8);

        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse("USD", hoy, 1, 20.50m, ahora, 1, null, 1));
        _awPort.ValoresVigentesAw["USD"] = 20.50m;

        // Act
        await worker.EjecutarCicloAsync(ahora);

        // Assert: no debe emitir Alerta 2
        _logger.Logs.Should().NotContain(l => l.Message.Contains("ALERTA_TC_NO_APLICADO_AW"));
    }

    [Fact]
    public async Task EjecutarCicloAsync_A_Las_09_Horas_Cuando_Aw_Difiere_De_Erp_Emite_Alerta_3()
    {
        // Arrange (09:00: resultado=1 pero A+W tiene 20.10 mientras ERP publicó 20.50)
        var worker = CrearWorker(new AwTipoCambioOptions
        {
            Habilitado = true,
            HoraLimitePublicacion = "18:00",
            HoraLimiteAplicacion = "09:00",
            Monedas = ["USD"]
        });

        var ahora = new DateTimeOffset(2026, 11, 8, 9, 15, 0, TimeSpan.FromHours(-6));
        var hoy = new DateOnly(2026, 11, 8);

        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse("USD", hoy, 1, 20.50m, ahora, 1, null, 1));
        _awPort.ValoresVigentesAw["USD"] = 20.10m; // Discrepancia!

        // Act
        await worker.EjecutarCicloAsync(ahora);

        // Assert: Alerta 3 emitida
        _logger.Logs.Should().Contain(l =>
            l.Level == LogLevel.Warning
            && l.Message.Contains("ALERTA_TC_DESFASE_ERP_AW")
            && l.Message.Contains("USD")
            && l.Message.Contains("ERP=20.50")
            && l.Message.Contains("A+W=20.10"));
    }

    [Fact]
    public async Task EjecutarCicloAsync_A_Las_09_Horas_Cuando_Aw_Coincide_Con_Erp_No_Emite_Alerta_3()
    {
        // Arrange: USD resultado=1 y valor coincide
        var worker = CrearWorker(new AwTipoCambioOptions
        {
            Habilitado = true,
            HoraLimitePublicacion = "18:00",
            HoraLimiteAplicacion = "09:00",
            Monedas = ["USD"]
        });

        var ahora = new DateTimeOffset(2026, 11, 8, 9, 15, 0, TimeSpan.FromHours(-6));
        var hoy = new DateOnly(2026, 11, 8);

        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse("USD", hoy, 1, 20.50m, ahora, 1, null, 1));
        _awPort.ValoresVigentesAw["USD"] = 0.04878049m; // VALUTAKURS = 1/20.50 a 8 decimales

        // Act
        await worker.EjecutarCicloAsync(ahora);

        // Assert: no debe emitir Alerta 3
        _logger.Logs.Should().NotContain(l => l.Message.Contains("ALERTA_TC_DESFASE_ERP_AW"));
    }

    [Fact]
    public async Task EjecutarCicloAsync_Valor_Real_Euro_De_Aw_Invertido_No_Emite_Alerta_3()
    {
        // Medido en A+W 2026-11-09: Euro VALUTAKURS 0.04804228 (≈ 20.815 MXN por EUR).
        var worker = CrearWorker(new AwTipoCambioOptions
        {
            Habilitado = true, HoraLimitePublicacion = "18:00", HoraLimiteAplicacion = "09:00", Monedas = ["EUR"]
        });
        var ahora = new DateTimeOffset(2026, 11, 9, 9, 15, 0, TimeSpan.FromHours(-6));
        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse("EUR", new DateOnly(2026, 11, 9), 1, 20.815m, ahora, 1, null, 1));
        _awPort.ValoresVigentesAw["EUR"] = 0.04804228m;

        await worker.EjecutarCicloAsync(ahora);

        _logger.Logs.Should().NotContain(l => l.Message.Contains("ALERTA_TC_DESFASE_ERP_AW"));
    }

    [Fact]
    public void ValutakursDe_invierte_y_redondea_half_up_a_8_decimales()
    {
        Millet.Integraciones.Aw.Infrastructure.TipoCambio.AwTipoCambioAdapter.ValutakursDe(20.815m).Should().Be(0.04804228m);
        Millet.Integraciones.Aw.Infrastructure.TipoCambio.AwTipoCambioAdapter.ValutakursDe(1m).Should().Be(1m);
        Millet.Integraciones.Aw.Infrastructure.TipoCambio.AwTipoCambioAdapter.ValutakursDe(18.7147m).Should().Be(0.05343393m);
    }

    [Fact]
    public async Task EjecutarCicloAsync_ControlFrecuencia_EmiteMaximoUnaAlertaPorTipoAlDia()
    {
        // Arrange
        var worker = CrearWorker(new AwTipoCambioOptions
        {
            Habilitado = true,
            HoraLimitePublicacion = "18:00",
            HoraLimiteAplicacion = "09:00",
            Monedas = ["USD"]
        });

        var dia1Hora18 = new DateTimeOffset(2026, 11, 8, 18, 0, 0, TimeSpan.FromHours(-6));
        var dia1Hora19 = new DateTimeOffset(2026, 11, 8, 19, 0, 0, TimeSpan.FromHours(-6));
        var dia2Hora18 = new DateTimeOffset(2026, 11, 9, 18, 0, 0, TimeSpan.FromHours(-6));

        // Act 1: Primer ciclo a las 18:00 del día 1 -> Emite Alerta 1
        await worker.EjecutarCicloAsync(dia1Hora18);

        _logger.Logs.Where(l => l.Message.Contains("ALERTA_TC_MANANA_SIN_PUBLICAR")).Should().HaveCount(1);

        // Act 2: Segundo ciclo a las 19:00 del mismo día 1 -> NO debe duplicar Alerta 1
        await worker.EjecutarCicloAsync(dia1Hora19);

        _logger.Logs.Where(l => l.Message.Contains("ALERTA_TC_MANANA_SIN_PUBLICAR")).Should().HaveCount(1);

        // Act 3: Ciclo al día siguiente (día 2 a las 18:00) -> Emite Alerta 1 para el nuevo día
        await worker.EjecutarCicloAsync(dia2Hora18);

        _logger.Logs.Where(l => l.Message.Contains("ALERTA_TC_MANANA_SIN_PUBLICAR")).Should().HaveCount(2);
    }

    [Fact]
    public async Task EjecutarCicloAsync_AntesDeHorasLimite_NoEmiteAlertas()
    {
        // Arrange (08:00: antes de 09:00 y antes de 18:00)
        var worker = CrearWorker(new AwTipoCambioOptions
        {
            Habilitado = true,
            HoraLimitePublicacion = "18:00",
            HoraLimiteAplicacion = "09:00",
            Monedas = ["USD"]
        });

        var ahora = new DateTimeOffset(2026, 11, 8, 8, 0, 0, TimeSpan.FromHours(-6));

        // Act
        await worker.EjecutarCicloAsync(ahora);

        // Assert: ninguna alerta emitida
        _logger.Logs.Should().NotContain(l => l.Message.Contains("ALERTA_TC_"));
    }

    private sealed class FakeTipoCambioRangoReadPort : ITipoCambioRangoReadPort
    {
        public List<TipoCambioRangoItem> Items { get; } = [];

        public Task<IReadOnlyList<TipoCambioRangoItem>> ObtenerRangoTipoCambioAsync(
            IReadOnlyList<string> monedas, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
        {
            var filtered = Items
                .Where(x => monedas.Contains(x.Moneda, StringComparer.OrdinalIgnoreCase) && x.Fecha >= desde && x.Fecha <= hasta)
                .ToList();
            return Task.FromResult<IReadOnlyList<TipoCambioRangoItem>>(filtered);
        }
    }

    private sealed class FakeAwTipoCambioPort : IAwTipoCambioPort
    {
        public List<AwTipoCambioFila> FilasPublicadas { get; } = [];
        public List<AwTipoCambioAcuse> AcusesExistentes { get; } = [];
        public Dictionary<string, decimal> ValoresVigentesAw { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<int?> PublicarFilaAsync(AwTipoCambioFila fila, CancellationToken ct = default)
        {
            FilasPublicadas.Add(fila);
            return Task.FromResult<int?>(1);
        }

        public Task<IReadOnlyList<AwTipoCambioAcuse>> ObtenerUltimosAcusesAsync(
            IReadOnlyList<string> monedas, DateOnly desde, CancellationToken ct = default)
        {
            var filtered = AcusesExistentes
                .Where(x => monedas.Contains(x.Moneda, StringComparer.OrdinalIgnoreCase) && x.FechaAplicacion >= desde)
                .ToList();
            return Task.FromResult<IReadOnlyList<AwTipoCambioAcuse>>(filtered);
        }

        public Task<decimal?> ObtenerTipoCambioVigenteAwAsync(string moneda, CancellationToken ct = default)
        {
            if (ValoresVigentesAw.TryGetValue(moneda, out var valor))
            {
                return Task.FromResult<decimal?>(valor);
            }
            return Task.FromResult<decimal?>(null);
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
