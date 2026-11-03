using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Application.Monedas;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.UnitTests.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.UnitTests.Catalogos;

/// <summary>K10.6: comando de descarga con un cliente falso del SIE de Banxico.</summary>
public sealed class DescargarTipoCambioBanxicoTests
{
    private static DateOnly Oct(int dia) => new(2026, 10, dia);

    /// <summary>Hora local de México (UTC-6).</summary>
    private static DateTimeOffset Local(int dia, int hora) => new(2026, 10, dia, hora, 0, 0, TimeSpan.FromHours(-6));

    private static readonly Dictionary<DateOnly, decimal> FixUsd = new()
    {
        [Oct(14)] = 18.4888m, [Oct(15)] = 18.4250m, [Oct(16)] = 18.4930m, [Oct(19)] = 18.4253m,
        [Oct(20)] = 18.4200m, [Oct(21)] = 18.6425m,
    };

    private static readonly Dictionary<DateOnly, decimal> Eur = new()
    {
        [Oct(14)] = 20.6618m, [Oct(15)] = 20.8213m, [Oct(16)] = 20.6704m, [Oct(19)] = 20.6451m,
        [Oct(20)] = 20.9402m, [Oct(21)] = 20.9717m,
    };

    private static readonly Dictionary<DateOnly, decimal> Cad = new()
    {
        [Oct(14)] = 12.9955m, [Oct(15)] = 13.0395m, [Oct(16)] = 13.0804m, [Oct(19)] = 13.1216m,
        [Oct(20)] = 13.2503m, [Oct(21)] = 13.3458m,
    };

    private sealed class BanxicoFalso : IBanxicoSiePort
    {
        public Dictionary<string, Dictionary<DateOnly, decimal>> Series { get; } = new()
        {
            [SeriesBanxico.FixUsd] = FixUsd, [SeriesBanxico.Eur] = Eur, [SeriesBanxico.Cad] = Cad,
        };

        public Exception? Falla { get; set; }

        public Task<IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, decimal>>> ObtenerSeriesAsync(
            IReadOnlyCollection<string> series, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken)
        {
            if (Falla is not null) throw Falla;
            IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, decimal>> resultado = series
                .Where(Series.ContainsKey)
                .ToDictionary(s => s, s => (IReadOnlyDictionary<DateOnly, decimal>)Series[s]
                    .Where(kv => kv.Key >= desde && kv.Key <= hasta).ToDictionary());
            return Task.FromResult(resultado);
        }
    }

    private sealed class Escenario : IDisposable
    {
        public CompartidoDbContext Db { get; } = new(
            new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresa());
        public BanxicoFalso Banxico { get; } = new();
        public FakeClock Reloj { get; } = new();
        public Dictionary<string, Guid> Monedas { get; } = [];

        public Escenario(params string[] monedas)
        {
            foreach (var codigo in monedas)
            {
                var moneda = new Moneda(Guid.NewGuid(), codigo, codigo, 2);
                Db.Monedas.Add(moneda);
                Monedas[codigo] = moneda.Id;
            }
            Db.SaveChanges();
        }

        public Task<DescargaTipoCambioResultado> CorrerAsync(DateTimeOffset ahora)
        {
            Reloj.UtcNow = ahora.ToUniversalTime();
            return new DescargarTipoCambioBanxicoHandler(Db, Banxico, Reloj)
                .Handle(new DescargarTipoCambioBanxicoCommand(), default);
        }

        public decimal? Valor(string moneda, DateOnly fecha) => Db.TiposCambio.AsNoTracking()
            .Where(t => t.MonedaId == Monedas[moneda] && t.Fecha == fecha)
            .Select(t => (decimal?)t.ValorEnMxn).SingleOrDefault();

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task La_factura_del_miercoles_21_a_las_9_ya_tiene_el_fix_del_lunes_19()
    {
        using var e = new Escenario("USD", "EUR", "CAD");
        e.Banxico.Series[SeriesBanxico.FixUsd] = FixUsd.Where(kv => kv.Key <= Oct(20)).ToDictionary();

        // Corrida de la tarde del martes 20 (FIX del 20 publicado): deja listo el miércoles.
        await e.CorrerAsync(Local(20, 13));

        e.Valor("USD", Oct(21)).Should().Be(18.4253m);
        e.Valor("EUR", Oct(21)).Should().Be(20.6451m);
        e.Valor("CAD", Oct(21)).Should().Be(13.1216m);
        e.Db.TiposCambio.Should().OnlyContain(t => t.Origen == OrigenTipoCambio.Banxico);
    }

    [Fact]
    public async Task El_viernes_por_la_tarde_deja_registrados_sabado_domingo_y_lunes_con_el_fix_del_jueves()
    {
        using var e = new Escenario("USD", "EUR", "CAD");
        e.Banxico.Series[SeriesBanxico.FixUsd] = FixUsd.Where(kv => kv.Key <= Oct(16)).ToDictionary();

        var resultado = await e.CorrerAsync(Local(16, 13));

        resultado.CalendarioConocidoHasta.Should().Be(Oct(18));
        foreach (var dia in new[] { 17, 18, 19 })
        {
            e.Valor("USD", Oct(dia)).Should().Be(18.4250m);
            e.Valor("EUR", Oct(dia)).Should().Be(20.8213m);
            e.Valor("CAD", Oct(dia)).Should().Be(13.0395m);
        }
        e.Valor("USD", Oct(20)).Should().BeNull("el martes depende de si el lunes es hábil");
        resultado.Monedas.Should().OnlyContain(m => m.UltimaFecha == Oct(19));
    }

    [Fact]
    public async Task Repetir_la_descarga_no_duplica_y_corrige_solo_valores_propios()
    {
        using var e = new Escenario("USD");
        await e.CorrerAsync(Local(21, 9));
        var registrados = e.Db.TiposCambio.Count();

        e.Banxico.Series[SeriesBanxico.FixUsd] = new Dictionary<DateOnly, decimal>(FixUsd) { [Oct(19)] = 18.4300m };
        var resultado = await e.CorrerAsync(Local(21, 9));

        e.Db.TiposCambio.Count().Should().Be(registrados);
        e.Valor("USD", Oct(21)).Should().Be(18.4300m);
        resultado.Monedas.Single(m => m.Moneda == "USD").Corregidos.Should().Be(1);
    }

    [Fact]
    public async Task Un_valor_capturado_a_mano_no_se_sobrescribe_y_se_reporta()
    {
        using var e = new Escenario("USD");
        e.Db.TiposCambio.Add(new TipoCambio(Guid.NewGuid(), e.Monedas["USD"], Oct(21), 18.5000m));
        await e.Db.SaveChangesAsync();

        var resultado = await e.CorrerAsync(Local(21, 9));

        e.Valor("USD", Oct(21)).Should().Be(18.5000m);
        resultado.Monedas.Single(m => m.Moneda == "USD").Conflictos.Should().Equal(Oct(21));
    }

    [Fact]
    public async Task Sin_moneda_en_el_catalogo_se_reporta_y_las_demas_se_guardan()
    {
        using var e = new Escenario("USD");

        var resultado = await e.CorrerAsync(Local(21, 9));

        resultado.Monedas.Where(m => m.MonedaNoRegistrada).Select(m => m.Moneda).Should().BeEquivalentTo("EUR", "CAD");
        e.Valor("USD", Oct(21)).Should().Be(18.4253m);
    }

    [Fact]
    public async Task Si_banxico_no_devuelve_fix_falla_sin_guardar_nada()
    {
        using var e = new Escenario("USD");
        e.Banxico.Series[SeriesBanxico.FixUsd] = [];

        var act = () => e.CorrerAsync(Local(21, 9));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("BANXICO_SIN_FIX");
        e.Db.TiposCambio.Should().BeEmpty();
    }

    [Fact]
    public async Task Si_banxico_no_responde_la_excepcion_sube_para_que_el_worker_reintente()
    {
        using var e = new Escenario("USD");
        e.Banxico.Falla = new HttpRequestException("SIE fuera de servicio");

        var act = () => e.CorrerAsync(Local(21, 9));

        await act.Should().ThrowAsync<HttpRequestException>();
        e.Db.TiposCambio.Should().BeEmpty();
    }
}
