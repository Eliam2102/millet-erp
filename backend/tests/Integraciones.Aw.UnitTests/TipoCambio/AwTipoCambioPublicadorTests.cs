using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Compartido.Domain.Ports;
using Millet.Integraciones.Aw.Application.TipoCambio;

namespace Millet.Integraciones.Aw.UnitTests.TipoCambio;

public class AwTipoCambioPublicadorTests
{
    private static readonly string[] MonedasEsperadas = ["USD", "EUR", "CAD"];
    private readonly FakeTipoCambioRangoReadPort _rangoReadPort = new();
    private readonly FakeAwTipoCambioPort _awPort = new();

    private AwTipoCambioPublicador CrearPublicador(AwTipoCambioOptions? options = null)
    {
        var opt = Options.Create(options ?? new AwTipoCambioOptions { Habilitado = true });
        return new AwTipoCambioPublicador(
            _rangoReadPort,
            _awPort,
            opt,
            NullLogger<AwTipoCambioPublicador>.Instance);
    }

    [Fact]
    public async Task PublicarAsync_CanalDeshabilitado_RetornaSinInsertar()
    {
        // Arrange
        var publicador = CrearPublicador(new AwTipoCambioOptions { Habilitado = false });
        var fecha = new DateOnly(2026, 11, 8);
        _rangoReadPort.Items.Add(new TipoCambioRangoItem("USD", fecha, 20.50m, "Banxico"));

        // Act
        var resultado = await publicador.PublicarAsync(fecha);

        // Assert
        resultado.Habilitado.Should().BeFalse();
        resultado.Insertadas.Should().Be(0);
        resultado.Omitidas.Should().Be(0);
        resultado.Mensaje.Should().Contain("deshabilitado");
        _awPort.FilasPublicadas.Should().BeEmpty();
    }

    [Fact]
    public async Task PublicarAsync_RangoYMonedasConfiguradas_ConsultaDesdeMenos7HastaMas4Dias()
    {
        // Arrange
        var publicador = CrearPublicador();
        var fecha = new DateOnly(2026, 11, 8);

        // Act
        await publicador.PublicarAsync(fecha);

        // Assert
        _rangoReadPort.UltimoDesdeSolicitado.Should().Be(new DateOnly(2026, 11, 1));
        _rangoReadPort.UltimoHastaSolicitado.Should().Be(new DateOnly(2026, 11, 12));
        _rangoReadPort.UltimasMonedasSolicitadas.Should().BeEquivalentTo(MonedasEsperadas);

        _awPort.UltimoDesdeConsultado.Should().Be(new DateOnly(2026, 11, 1));
        _awPort.UltimasMonedasConsultadas.Should().BeEquivalentTo(MonedasEsperadas);
    }

    [Fact]
    public async Task PublicarAsync_SinRegistrosPrevios_InsertaVersion1ParaMonedasYFechas()
    {
        // Arrange
        var publicador = CrearPublicador();
        var fecha = new DateOnly(2026, 11, 8);

        _rangoReadPort.Items.Add(new TipoCambioRangoItem("USD", fecha, 20.50m, "Banxico"));
        _rangoReadPort.Items.Add(new TipoCambioRangoItem("EUR", fecha, 22.10m, "Banxico"));
        _rangoReadPort.Items.Add(new TipoCambioRangoItem("CAD", fecha, 15.30m, "Manual"));

        // Act
        var resultado = await publicador.PublicarAsync(fecha);

        // Assert
        resultado.Habilitado.Should().BeTrue();
        resultado.Insertadas.Should().Be(3);
        resultado.Omitidas.Should().Be(0);

        _awPort.FilasPublicadas.Should().HaveCount(3);
        _awPort.FilasPublicadas.Should().Contain(f => f.Moneda == "USD" && f.FechaAplicacion == fecha && f.Version == 1 && f.Valor == 20.50m && f.Origen == "Banxico");
        _awPort.FilasPublicadas.Should().Contain(f => f.Moneda == "EUR" && f.FechaAplicacion == fecha && f.Version == 1 && f.Valor == 22.10m && f.Origen == "Banxico");
        _awPort.FilasPublicadas.Should().Contain(f => f.Moneda == "CAD" && f.FechaAplicacion == fecha && f.Version == 1 && f.Valor == 15.30m && f.Origen == "Manual");
    }

    [Fact]
    public async Task PublicarAsync_Idempotencia_MismoValorExistente_OmiteSinInsertar()
    {
        // Arrange
        var publicador = CrearPublicador();
        var fecha = new DateOnly(2026, 11, 8);

        _rangoReadPort.Items.Add(new TipoCambioRangoItem("USD", fecha, 20.50m, "Banxico"));
        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse(
            "USD", fecha, 1, 20.50m, DateTimeOffset.UtcNow, 1, null, 1));

        // Act
        var resultado = await publicador.PublicarAsync(fecha);

        // Assert
        resultado.Habilitado.Should().BeTrue();
        resultado.Insertadas.Should().Be(0);
        resultado.Omitidas.Should().Be(1);
        _awPort.FilasPublicadas.Should().BeEmpty();
    }

    [Fact]
    public async Task PublicarAsync_Correccion_ValorDifiere_InsertaVersionSiguiente()
    {
        // Arrange
        var publicador = CrearPublicador();
        var fecha = new DateOnly(2026, 11, 8);

        _rangoReadPort.Items.Add(new TipoCambioRangoItem("USD", fecha, 20.75m, "Banxico"));
        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse(
            "USD", fecha, 1, 20.50m, DateTimeOffset.UtcNow, 1, null, 1));

        // Act
        var resultado = await publicador.PublicarAsync(fecha);

        // Assert
        resultado.Habilitado.Should().BeTrue();
        resultado.Insertadas.Should().Be(1);
        resultado.Omitidas.Should().Be(0);

        _awPort.FilasPublicadas.Should().HaveCount(1);
        var fila = _awPort.FilasPublicadas.Single();
        fila.Moneda.Should().Be("USD");
        fila.FechaAplicacion.Should().Be(fecha);
        fila.Version.Should().Be(2);
        fila.Valor.Should().Be(20.75m);
        fila.Origen.Should().Be("Banxico");
    }

    [Fact]
    public async Task PublicarAsync_MultiplesVersionesPrevias_ComparaContraLaVersionMayor()
    {
        // Arrange
        var publicador = CrearPublicador();
        var fecha = new DateOnly(2026, 11, 8);

        // Hay versión 1 con 20.00 y versión 2 corregida con 20.50
        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse("USD", fecha, 1, 20.00m, DateTimeOffset.UtcNow, 3, "Superada", 0));
        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse("USD", fecha, 2, 20.50m, DateTimeOffset.UtcNow, 1, null, 1));

        // Catálogo sigue con 20.50 -> debe omitir porque ya rige versión 2
        _rangoReadPort.Items.Add(new TipoCambioRangoItem("USD", fecha, 20.50m, "Banxico"));

        // Act
        var resultado = await publicador.PublicarAsync(fecha);

        // Assert
        resultado.Insertadas.Should().Be(0);
        resultado.Omitidas.Should().Be(1);
        _awPort.FilasPublicadas.Should().BeEmpty();
    }

    [Fact]
    public async Task PublicarAsync_MultiplesVersionesPrevias_SiCambiaIncrementaDesdeVersionMayor()
    {
        // Arrange
        var publicador = CrearPublicador();
        var fecha = new DateOnly(2026, 11, 8);

        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse("USD", fecha, 1, 20.00m, DateTimeOffset.UtcNow, 3, "Superada", 0));
        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse("USD", fecha, 2, 20.50m, DateTimeOffset.UtcNow, 1, null, 1));

        // Catálogo cambia a 20.80 -> debe insertar versión 3
        _rangoReadPort.Items.Add(new TipoCambioRangoItem("USD", fecha, 20.80m, "Manual"));

        // Act
        var resultado = await publicador.PublicarAsync(fecha);

        // Assert
        resultado.Insertadas.Should().Be(1);
        resultado.Omitidas.Should().Be(0);
        _awPort.FilasPublicadas.Should().HaveCount(1);
        _awPort.FilasPublicadas[0].Version.Should().Be(3);
        _awPort.FilasPublicadas[0].Valor.Should().Be(20.80m);
    }

    [Fact]
    public async Task PublicarAsync_MezclaDeMonedasYFechas_InsertaYOmitSegunCorresponda()
    {
        // Arrange
        var publicador = CrearPublicador();
        var f1 = new DateOnly(2026, 11, 7);
        var f2 = new DateOnly(2026, 11, 8);

        // f1 USD ya existe con mismo valor (omite)
        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse("USD", f1, 1, 20.40m, DateTimeOffset.UtcNow, 1, null, 1));
        _rangoReadPort.Items.Add(new TipoCambioRangoItem("USD", f1, 20.40m, "Banxico"));

        // f2 USD no existe (inserta v1)
        _rangoReadPort.Items.Add(new TipoCambioRangoItem("USD", f2, 20.55m, "Banxico"));

        // f2 EUR existe pero con valor distinto (inserta v2)
        _awPort.AcusesExistentes.Add(new AwTipoCambioAcuse("EUR", f2, 1, 22.00m, DateTimeOffset.UtcNow, 1, null, 1));
        _rangoReadPort.Items.Add(new TipoCambioRangoItem("EUR", f2, 22.30m, "Banxico"));

        // Act
        var resultado = await publicador.PublicarAsync(f2);

        // Assert
        resultado.Insertadas.Should().Be(2);
        resultado.Omitidas.Should().Be(1);
        _awPort.FilasPublicadas.Should().HaveCount(2);
        _awPort.FilasPublicadas.Should().Contain(f => f.Moneda == "USD" && f.FechaAplicacion == f2 && f.Version == 1);
        _awPort.FilasPublicadas.Should().Contain(f => f.Moneda == "EUR" && f.FechaAplicacion == f2 && f.Version == 2 && f.Valor == 22.30m);
    }

    private sealed class FakeTipoCambioRangoReadPort : ITipoCambioRangoReadPort
    {
        public List<TipoCambioRangoItem> Items { get; } = [];
        public IReadOnlyList<string>? UltimasMonedasSolicitadas { get; private set; }
        public DateOnly? UltimoDesdeSolicitado { get; private set; }
        public DateOnly? UltimoHastaSolicitado { get; private set; }

        public Task<IReadOnlyList<TipoCambioRangoItem>> ObtenerRangoTipoCambioAsync(
            IReadOnlyList<string> monedas, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
        {
            UltimasMonedasSolicitadas = monedas;
            UltimoDesdeSolicitado = desde;
            UltimoHastaSolicitado = hasta;

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
        public IReadOnlyList<string>? UltimasMonedasConsultadas { get; private set; }
        public DateOnly? UltimoDesdeConsultado { get; private set; }

        public Task<int?> PublicarFilaAsync(AwTipoCambioFila fila, CancellationToken ct = default)
        {
            FilasPublicadas.Add(fila);
            return Task.FromResult<int?>(1);
        }

        public Task<IReadOnlyList<AwTipoCambioAcuse>> ObtenerUltimosAcusesAsync(
            IReadOnlyList<string> monedas, DateOnly desde, CancellationToken ct = default)
        {
            UltimasMonedasConsultadas = monedas;
            UltimoDesdeConsultado = desde;

            var filtered = AcusesExistentes
                .Where(x => monedas.Contains(x.Moneda, StringComparer.OrdinalIgnoreCase) && x.FechaAplicacion >= desde)
                .ToList();
            return Task.FromResult<IReadOnlyList<AwTipoCambioAcuse>>(filtered);
        }

        public Task<decimal?> ObtenerTipoCambioVigenteAwAsync(string moneda, CancellationToken ct = default)
        {
            return Task.FromResult<decimal?>(null);
        }
    }
}
