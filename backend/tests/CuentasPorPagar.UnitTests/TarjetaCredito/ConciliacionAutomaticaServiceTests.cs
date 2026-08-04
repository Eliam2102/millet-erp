using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.CuentasPorPagar.Infrastructure.TarjetaCredito;

namespace Millet.CuentasPorPagar.UnitTests.TarjetaCredito;

/// <summary>
/// F7-PR5: tests del algoritmo de match score 0-100 (§7 anexo TC) y
/// del servicio de conciliación que lo aplica con restricción 1:1.
/// </summary>
public sealed class ConciliacionAutomaticaServiceTests
{
    private static Tarjeta CrearTarjeta() =>
        Tarjeta.Crear(
            empresaId: Guid.NewGuid(),
            emisora: "Amex",
            perfilParser: "AMEX_MX",
            numero: NumeroTarjetaEnmascarado.FromUltimosCuatro("1234"),
            nombreAlias: "Amex Corp",
            titularId: Guid.NewGuid(),
            bancoProveedorId: Guid.NewGuid(),
            limiteCreditoMxn: 100000m,
            monedaDefault: "MXN",
            diaCorte: 15,
            diaLimitePago: 20,
            vigenciaDesde: new DateOnly(2026, 1, 1));

    private static MovimientoTarjetaCredito MovSinCfdi(
        Tarjeta tarjeta,
        DateOnly fecha,
        decimal monto,
        string merchant) =>
        MovimientoTarjetaCredito.CapturarCompraSinCfdi(
            empresaId: tarjeta.EmpresaId, tarjeta: tarjeta,
            usuarioQueUsoId: tarjeta.TitularId,
            fechaMovimiento: fecha,
            montoOriginal: monto, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: merchant, descripcionLibre: null,
            conceptoContable: "Test", ticketBlobRef: null);

    // --- Score componente por componente ---

    [Fact]
    public void Score_match_exacto_monto_fecha_merchant_da_100()
    {
        var t = CrearTarjeta();
        var mov = MovSinCfdi(t, new DateOnly(2026, 5, 10), 1160m, "STARBUCKS CDMX");
        var ec = EstadoCuentaTc.Crear(t.EmpresaId, t.Id, new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31), new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 25));
        var linea = ec.AgregarLinea(1, new DateOnly(2026, 5, 10), 1160m, "MXN", 1160m,
            "STARBUCKS CDMX", null, "Compra");

        var score = ConciliacionAutomaticaService.CalcularScore(linea, mov);
        score.Should().Be(100m); // 50 (monto exacto) + 25 (fecha exacta) + 25 (merchant idéntico)
    }

    [Fact]
    public void Score_monto_proximo_2pct_30_puntos()
    {
        var t = CrearTarjeta();
        var mov = MovSinCfdi(t, new DateOnly(2026, 5, 10), 1000m, "AMAZON");
        var ec = EstadoCuentaTc.Crear(t.EmpresaId, t.Id, new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31), new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 25));
        // Banco aplica 1.5% extra
        var linea = ec.AgregarLinea(1, new DateOnly(2026, 5, 10), 1015m, "MXN", 1015m,
            "AMAZON", null, "Compra");

        var score = ConciliacionAutomaticaService.CalcularScore(linea, mov);
        score.Should().Be(80m); // 30 (2%) + 25 (fecha) + 25 (merchant)
    }

    [Fact]
    public void Score_fechas_difieren_1_dia_15_puntos()
    {
        var t = CrearTarjeta();
        var mov = MovSinCfdi(t, new DateOnly(2026, 5, 10), 500m, "PEMEX");
        var ec = EstadoCuentaTc.Crear(t.EmpresaId, t.Id, new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31), new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 25));
        var linea = ec.AgregarLinea(1, new DateOnly(2026, 5, 11), 500m, "MXN", 500m,
            "PEMEX", null, "Compra");

        var score = ConciliacionAutomaticaService.CalcularScore(linea, mov);
        score.Should().Be(90m); // 50 + 15 + 25
    }

    [Fact]
    public void Score_merchant_distinto_baja_a_75()
    {
        var t = CrearTarjeta();
        var mov = MovSinCfdi(t, new DateOnly(2026, 5, 10), 500m, "STARBUCKS CDMX");
        var ec = EstadoCuentaTc.Crear(t.EmpresaId, t.Id, new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31), new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 25));
        var linea = ec.AgregarLinea(1, new DateOnly(2026, 5, 10), 500m, "MXN", 500m,
            "OXXO POLANCO", null, "Compra");

        var score = ConciliacionAutomaticaService.CalcularScore(linea, mov);
        // 50 (monto exacto) + 25 (fecha) + 0 (merchant muy distinto) = 75
        score.Should().Be(75m);
    }

    // --- Trigram similarity ---

    [Fact]
    public void TrigramSimilarity_string_identico_1()
    {
        ConciliacionAutomaticaService.TrigramSimilarity("STARBUCKS", "STARBUCKS")
            .Should().Be(1.0);
    }

    [Fact]
    public void TrigramSimilarity_strings_distintos_cero_o_bajo()
    {
        ConciliacionAutomaticaService.TrigramSimilarity("STARBUCKS", "OXXO")
            .Should().BeLessThan(0.3);
    }

    [Fact]
    public void TrigramSimilarity_strings_similares_alto()
    {
        // STARBUCKS vs STARBUCK son casi iguales — esperamos similarity > 0.6
        var sim = ConciliacionAutomaticaService.TrigramSimilarity("STARBUCKS CDMX", "STARBUCKS CDMX REFORMA");
        sim.Should().BeGreaterThan(0.50);
    }

    // --- Servicio: 1:1, omitidos, asignación greedy ---

    [Fact]
    public async Task ConciliarAsync_match_automatico_score_90_plus()
    {
        var t = CrearTarjeta();
        var mov = MovSinCfdi(t, new DateOnly(2026, 5, 10), 500m, "PEMEX");
        var ec = EstadoCuentaTc.Crear(t.EmpresaId, t.Id, new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31), new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 25));
        ec.AgregarLinea(1, new DateOnly(2026, 5, 10), 500m, "MXN", 500m, "PEMEX", null, "Compra");

        var svc = new ConciliacionAutomaticaService(NullLogger<ConciliacionAutomaticaService>.Instance);
        var result = await svc.ConciliarAsync(ec, [mov], CancellationToken.None);

        result.LineasMatchedAuto.Should().Be(1);
        result.LineasSugerencia.Should().Be(0);
        result.LineasSinMatch.Should().Be(0);
        result.TotalConciliadoMxn.Should().Be(500m);

        var linea = ec.Lineas.Single();
        linea.EstadoMatch.Should().Be(EstadoMatchLineaBanco.Matched);
        linea.MovimientoTcId.Should().Be(mov.Id);
    }

    [Fact]
    public async Task ConciliarAsync_sugerencia_score_60_a_89()
    {
        var t = CrearTarjeta();
        // mov merchant similar — similarity > 0.5 pero < 0.8 → +10 puntos
        var mov = MovSinCfdi(t, new DateOnly(2026, 5, 10), 500m, "STARBUCKS");
        var ec = EstadoCuentaTc.Crear(t.EmpresaId, t.Id, new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31), new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 25));
        ec.AgregarLinea(1, new DateOnly(2026, 5, 11), 500m, "MXN", 500m,
            "STARBUCKS REFORMA", null, "Compra");
        // 50 (monto exacto) + 15 (fecha 1 día) + 10 (merchant similar) = 75 → sugerencia

        var svc = new ConciliacionAutomaticaService(NullLogger<ConciliacionAutomaticaService>.Instance);
        var result = await svc.ConciliarAsync(ec, [mov], CancellationToken.None);

        result.LineasSugerencia.Should().Be(1);
        result.LineasMatchedAuto.Should().Be(0);
        var linea = ec.Lineas.Single();
        linea.EstadoMatch.Should().Be(EstadoMatchLineaBanco.Pendiente);
        linea.ScoreMatch.Should().BeInRange(60m, 89m);
    }

    [Fact]
    public async Task ConciliarAsync_sin_candidatos_marca_NoConciliado()
    {
        var t = CrearTarjeta();
        var ec = EstadoCuentaTc.Crear(t.EmpresaId, t.Id, new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31), new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 25));
        ec.AgregarLinea(1, new DateOnly(2026, 5, 10), 999m, "MXN", 999m, "DESCONOCIDO", null, "Compra");

        var svc = new ConciliacionAutomaticaService(NullLogger<ConciliacionAutomaticaService>.Instance);
        var result = await svc.ConciliarAsync(ec, [], CancellationToken.None);

        result.LineasSinMatch.Should().Be(1);
        ec.Lineas.Single().EstadoMatch.Should().Be(EstadoMatchLineaBanco.NoConciliado);
    }

    [Fact]
    public async Task ConciliarAsync_lineas_interes_no_buscan_match()
    {
        var t = CrearTarjeta();
        var mov = MovSinCfdi(t, new DateOnly(2026, 5, 10), 50m, "INTERES MORATORIO");
        var ec = EstadoCuentaTc.Crear(t.EmpresaId, t.Id, new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31), new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 25));
        ec.AgregarLinea(1, new DateOnly(2026, 5, 10), 50m, "MXN", 50m,
            "INTERES MORATORIO", null, "Interes");

        var svc = new ConciliacionAutomaticaService(NullLogger<ConciliacionAutomaticaService>.Instance);
        var result = await svc.ConciliarAsync(ec, [mov], CancellationToken.None);

        result.LineasOmitidasNoBuscanMatch.Should().Be(1);
        result.LineasMatchedAuto.Should().Be(0);
        result.LineasSinMatch.Should().Be(0);
    }

    [Fact]
    public async Task ConciliarAsync_uno_a_uno_no_reusa_movimientos()
    {
        var t = CrearTarjeta();
        var mov1 = MovSinCfdi(t, new DateOnly(2026, 5, 10), 500m, "PEMEX");
        var ec = EstadoCuentaTc.Crear(t.EmpresaId, t.Id, new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31), new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 25));
        // Dos líneas casi idénticas — solo una matchea
        ec.AgregarLinea(1, new DateOnly(2026, 5, 10), 500m, "MXN", 500m, "PEMEX", null, "Compra");
        ec.AgregarLinea(2, new DateOnly(2026, 5, 10), 500m, "MXN", 500m, "PEMEX", null, "Compra");

        var svc = new ConciliacionAutomaticaService(NullLogger<ConciliacionAutomaticaService>.Instance);
        var result = await svc.ConciliarAsync(ec, [mov1], CancellationToken.None);

        result.LineasMatchedAuto.Should().Be(1);
        // La segunda línea queda sin match porque el mov ya fue asignado.
        ec.Lineas.Count(l => l.EstadoMatch == EstadoMatchLineaBanco.NoConciliado).Should().Be(1);
    }
}
