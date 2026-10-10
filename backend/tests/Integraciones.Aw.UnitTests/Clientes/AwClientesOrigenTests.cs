using System.Text.RegularExpressions;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Infrastructure.Clientes;

namespace Millet.Integraciones.Aw.UnitTests.Clientes;

/// <summary>Mapper puro, fuente simulada y revisión estática del SQL (ADM-06 C1).</summary>
public sealed class AwClientesOrigenTests
{
    private static readonly DateTime Ahora = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IReadOnlyDictionary<string, string> SinMapeo = new Dictionary<string, string>();

    private static AwClienteOrigenFila Fila(
        int id = 1, string? name1 = "CLIENTE DEMO 001", string? waehrung = "PESOSMX",
        IReadOnlyList<AwCondicionCoincidencia>? cond = null,
        decimal? credito = null, string? ust = null, string? steuer = "XAXX010101000") => new(
        id, 1, name1, "  ", null, "CALLE DEMO 1", "CIUDAD DEMO", "00000", "DEMO", "MX", ust, steuer,
        "5500000000", null, "demo@example.invalid", "30 DIAS", waehrung, credito, null, null, 1, 0,
        new DateOnly(2026, 9, 1), new DateTime(2026, 9, 1, 10, 0, 0), cond ?? []);

    private static AwClienteMapeoResultado Mapear(AwClienteOrigenFila f, IReadOnlyDictionary<string, string>? m = null) =>
        AwClienteSnapshotMapper.Mapear(f, m ?? SinMapeo, Ahora);

    [Theory]
    [InlineData(false, null, null)]
    [InlineData(true, "XEXX010101000", "00000")]
    public void Fiscales_de_origen_solo_se_aplican_en_demo(bool demo, string? rfc, string? cp)
    {
        var s = AwClienteSnapshotMapper.Mapear(Fila(ust: "XEXX010101000"), SinMapeo, Ahora,
            aplicarFiscalesDeOrigen: demo).Snapshot!;
        s.Rfc.Should().Be(rfc);
        s.CodigoPostalFiscal.Should().Be(cp);
        s.CandidatoFiscalUstId.Should().Be("XEXX010101000");
    }

    [Fact]
    public void Alta_valida_mapea_campos_y_versiones()
    {
        var s = Mapear(Fila()).Snapshot!;
        s.ReferenciaExterna.Should().Be("1");
        s.RazonSocial.Should().Be("CLIENTE DEMO 001");
        s.NombreComercialOrigen.Should().Be("CLIENTE DEMO 001");
        s.Telefono.Should().Be("5500000000");
        s.CodigoPostalFiscal.Should().BeNull("PLZ no es CP fiscal");
        s.DomicilioCp.Should().Be("00000");
        s.LeidoEnUtc.Should().Be(Ahora);
        (s.VersionContrato, s.VersionMapeo).Should().Be(("1", "0-borrador"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Name1_vacio_es_fila_invalida(string? name1)
    {
        var r = Mapear(Fila(name1: name1));
        r.EsValido.Should().BeFalse();
        r.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Rfc_generico_en_dos_referencias_nunca_llena_Rfc()
    {
        var a = Mapear(Fila(1, ust: "XEXX010101000")).Snapshot!;
        var b = Mapear(Fila(2)).Snapshot!;
        a.Rfc.Should().BeNull();
        b.Rfc.Should().BeNull();
        a.CandidatoFiscalUstId.Should().Be("XEXX010101000");
        b.CandidatoFiscalSteuernummer.Should().Be("XAXX010101000");
        (a.ReferenciaExterna != b.ReferenciaExterna).Should().BeTrue();
    }

    [Fact]
    public void Condicion_una_coincidencia_se_resuelve_y_conserva_nulos()
    {
        var s = Mapear(Fila(cond: [new(6, 60)])).Snapshot!;
        (s.CondicionNumeroOrigen, s.DiasNominalesOrigen).Should().Be((6, 60));
        s.CondicionCodigoOrigen.Should().Be("30 DIAS");

        var nulo = Mapear(Fila(cond: [new(null, null)])).Snapshot!;
        nulo.CondicionNumeroOrigen.Should().BeNull();
        nulo.DiasNominalesOrigen.Should().BeNull();

        var cero = Mapear(Fila(cond: [new(1, 0)])).Snapshot!;
        cero.DiasNominalesOrigen.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Condicion_sin_o_multiples_coincidencias_queda_pendiente(int n)
    {
        var cond = Enumerable.Range(1, n).Select(i => new AwCondicionCoincidencia(i, i * 10)).ToList();
        var s = Mapear(Fila(cond: cond)).Snapshot!;
        s.CondicionNumeroOrigen.Should().BeNull();
        s.DiasNominalesOrigen.Should().BeNull();
        s.CondicionCodigoOrigen.Should().Be("30 DIAS");
    }

    [Fact]
    public void Moneda_solo_se_normaliza_con_MapeoMoneda()
    {
        var sin = Mapear(Fila()).Snapshot!;
        (sin.MonedaCodigoOrigen, sin.MonedaNormalizada, sin.MonedaDefault).Should().Be(("PESOSMX", null, null));

        var mapeo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["pesosmx"] = "MXN" };
        var con = Mapear(Fila(), mapeo).Snapshot!;
        (con.MonedaCodigoOrigen, con.MonedaNormalizada, con.MonedaDefault).Should().Be(("PESOSMX", "MXN", "MXN"));

        var desconocida = Mapear(Fila(waehrung: "<indf>"), mapeo).Snapshot!;
        (desconocida.MonedaCodigoOrigen, desconocida.MonedaNormalizada).Should().Be(("<indf>", null));
    }

    [Fact]
    public void Credito_nulo_no_es_cero()
    {
        Mapear(Fila(credito: null)).Snapshot!.CreditoReferenciaLimite.Should().BeNull();
        Mapear(Fila(credito: 0m)).Snapshot!.CreditoReferenciaLimite.Should().Be(0m);
    }

    // ---- Simulado ----

    private static AwClientesOrigenSimulado Simulado(int n) =>
        new(Enumerable.Range(1, n).Reverse().Select(i => Fila(i * 10)));

    [Fact]
    public async Task Simulado_pagina_estable_por_id_con_cursor()
    {
        var o = Simulado(5);
        var p1 = await o.LeerPaginaAsync(null, 2, default);
        p1.Filas.Select(f => f.Id).Should().Equal(10, 20);
        p1.SiguienteCursor.Should().Be("20");

        var p2 = await o.LeerPaginaAsync(p1.SiguienteCursor, 2, default);
        p2.Filas.Select(f => f.Id).Should().Equal(30, 40);

        var p3 = await o.LeerPaginaAsync(p2.SiguienteCursor, 2, default);
        p3.Filas.Select(f => f.Id).Should().Equal(50);
        p3.SiguienteCursor.Should().BeNull();
    }

    [Fact]
    public async Task Simulado_acota_lote_a_500()
    {
        var p = await Simulado(600).LeerPaginaAsync(null, 10_000, default);
        p.Filas.Should().HaveCount(500);
        p.SiguienteCursor.Should().Be("5000");
    }

    [Fact]
    public async Task Simulado_referencia_ausente_devuelve_null()
    {
        var o = Simulado(2);
        (await o.LeerPorReferenciaAsync("20", default))!.Id.Should().Be(20);
        (await o.LeerPorReferenciaAsync("999", default)).Should().BeNull();
        (await o.LeerPorReferenciaAsync("abc", default)).Should().BeNull();
    }

    [Fact]
    public async Task Simulado_carga_formato_de_fixtures_y_resuelve_condicion()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Clientes", "Fixtures", "condicion-sin-coincidencia-o-duplicada.json");
        var o = AwClientesOrigenSimulado.DesdeArchivo(ruta);
        var p = await o.LeerPaginaAsync(null, 100, default);
        p.Filas.Select(f => f.Id).Should().Equal(1003, 1004);
        p.Filas[0].CondicionCoincidencias.Should().BeEmpty();     // "30 DIAS" sin catalogo
        p.Filas[1].CondicionCoincidencias.Should().HaveCount(2);  // REPARTO duplicado
        p.Filas[0].KreditLimit.Should().Be(1000.00m);
    }

    // ---- SQL estatico ----

    [Theory]
    [InlineData(AwClientesSqlOrigen.SqlPagina)]
    [InlineData(AwClientesSqlOrigen.SqlReferencia)]
    public void Sql_es_solo_lectura_con_columnas_explicitas(string sql)
    {
        sql.Should().NotMatchRegex(@"SELECT\s+(\w+\.)?\*");
        sql.Should().NotMatchRegex(@"(?i)\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|EXEC)\b");
        sql.Should().Contain("TOP").And.Contain("ORDER BY").And.Contain("@cursor");
        Regex.IsMatch(sql, @"\*").Should().BeFalse();
        sql.Should().Contain("SYSADM.KU_KUNDEN").And.Contain("SYSADM.KA_ZAHLBED").And.NotContain("dbo.");
    }

    [Fact]
    public void Sql_de_pagina_parametriza_lote()
    {
        AwClientesSqlOrigen.SqlPagina.Should().Contain("TOP (@n)");
    }
}
