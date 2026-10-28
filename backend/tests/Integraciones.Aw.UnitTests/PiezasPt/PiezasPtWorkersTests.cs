using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.PiezasPt;
using Millet.Integraciones.Aw.Infrastructure.PiezasPt;
using Millet.ProductoTerminado.Application.Escaneos;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.UnitTests.PiezasPt;

public sealed class PiezasPtWorkersTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Apagado_o_sin_empresa_no_crea_scope(bool habilitada, bool conEmpresa)
    {
        var o = Options.Create(new PiezasPtOptions { EntradaHabilitada = habilitada, EmpresaId = conEmpresa ? Guid.NewGuid() : null });
        using var worker = new PiezasPtEntradaWorker(new Prohibido(), o, NullLogger<PiezasPtEntradaWorker>.Instance);
        Assert.Equal(0, await worker.CorrerUnaVezAsync(CancellationToken.None));
    }

    [Fact]
    public void Clave_de_candado_distinta_por_empresa() =>
        Assert.NotEqual(PiezasPtEntradaWorker.ClaveCandado(Guid.NewGuid()), PiezasPtEntradaWorker.ClaveCandado(Guid.NewGuid()));

    [Fact]
    public async Task Filtra_no_terminadas_y_estaciones_fuera_de_lista()
    {
        var h = Armar(["2821-2840"], [Fila(1, 2821, true), Fila(2, 2821, false), Fila(3, 2900, true)]);
        Assert.Equal(1, await h.Worker.CicloAsync(h.Sp, default));
        Assert.Single(h.Comandos); Assert.Equal("E1", h.Comandos[0].Etiqueta);
        Assert.Equal(7, h.Comandos[0].MaxIntentos);
    }

    [Fact]
    public async Task Estacion_2900_se_envia_si_esta_configurada()
    {
        var h = Armar(["2900"], [Fila(1, 2900, true)]);
        Assert.Equal(1, await h.Worker.CicloAsync(h.Sp, default));
    }

    [Fact]
    public async Task Lista_vacia_no_llama_al_lector()
    {
        var h = Armar([], [Fila(1, 2821, true)]);
        Assert.Equal(0, await h.Worker.CicloAsync(h.Sp, default));
        Assert.Empty(h.Reader.Llamadas);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Marca_de_agua_menos_traslape_o_inicio_2000(bool conMarca)
    {
        var marca = new DateTime(2026, 5, 1, 12, 0, 0);
        var h = Armar(["2821"], [], conMarca ? marca : null);
        await h.Worker.CicloAsync(h.Sp, default);
        Assert.Equal(conMarca ? marca.AddMinutes(-10) : new DateTime(2000, 1, 1).AddMinutes(-10), h.Reader.Llamadas[0].Desde);
    }

    [Fact]
    public async Task Pagina_por_lotes_y_reintenta_pospuestos_una_vez_antes_de_leer()
    {
        var filas = Enumerable.Range(1, 450).Select(i => Fila(i, 2821, true)).ToArray();
        var h = Armar(["2821"], filas, batch: 200);
        Assert.Equal(450, await h.Worker.CicloAsync(h.Sp, default));
        Assert.Equal(3, h.Reader.Llamadas.Count);
        Assert.Single(h.Reintentos);
        Assert.Equal(450, h.Comandos.Count);
    }

    [Fact]
    public async Task Fila_que_falla_no_detiene_el_resto()
    {
        var h = Armar(["2821"], [Fila(1, 2821, true), Fila(2, 2821, true), Fila(3, 2821, true)], falla: "E2");
        Assert.Equal(2, await h.Worker.CicloAsync(h.Sp, default));
        Assert.Equal(["E1", "E3"], h.Comandos.Select(c => c.Etiqueta).ToArray());
    }

    private static EscaneoOrigen Fila(int i, int esnr, bool fertig) =>
        new($"E{i}", "100", 1, 1, "1", esnr, fertig, null, new DateTime(2026, 5, 1).AddSeconds(i), null);

    private static Arnes Armar(string[] estaciones, EscaneoOrigen[] filas, DateTime? marca = null, int batch = 200, string? falla = null)
    {
        var a = new Arnes(filas);
        var sc = new ServiceCollection();
        sc.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<RegistrarEscaneoPiezaCommand>());
        sc.AddSingleton<IRequestHandler<RegistrarEscaneoPiezaCommand, EscaneoResultadoDto>>(new Registrar(a, falla));
        sc.AddSingleton<IRequestHandler<ReintentarPospuestosCommand, int>>(new Reintentar(a));
        sc.AddSingleton<IRequestHandler<ObtenerMarcaAguaEscaneosQuery, DateTime?>>(new Marca(marca));
        sc.AddSingleton<IPiezasPtOrigenReader>(a.Reader);
        sc.AddScoped<ICurrentEmpresaContext, FakeEmpresa>();
        sc.AddScoped<IAuditOriginContext, FakeOrigen>();
        sc.AddScoped<IAuditCorrelationContext, FakeCorrelacion>();
        a.Sp = sc.BuildServiceProvider();
        a.Worker = new PiezasPtEntradaWorker(a.Sp.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PiezasPtOptions { EmpresaId = Empresa, EstacionesPt = estaciones, BatchSize = batch, MaxIntentos = 7 }),
            NullLogger<PiezasPtEntradaWorker>.Instance);
        return a;
    }

    private sealed class Arnes(EscaneoOrigen[] filas)
    {
        public FakeReader Reader { get; } = new(filas);
        public List<RegistrarEscaneoPiezaCommand> Comandos { get; } = [];
        public List<ReintentarPospuestosCommand> Reintentos { get; } = [];
        public IServiceProvider Sp { get; set; } = null!;
        public PiezasPtEntradaWorker Worker { get; set; } = null!;
    }

    private sealed class FakeReader(EscaneoOrigen[] filas) : IPiezasPtOrigenReader
    {
        public List<(DateTime Desde, (DateTime, string)? Cursor)> Llamadas { get; } = [];
        public Task<string> OrigenActualAsync(CancellationToken ct) => Task.FromResult("Demo");
        public Task<IReadOnlyList<EscaneoOrigen>> LeerAsync(DateTime desde, (DateTime Tiempo, string Etiqueta)? despuesDe,
            IReadOnlyList<(int Desde, int Hasta)> estaciones, int max, CancellationToken ct)
        {
            Llamadas.Add((desde, despuesDe));
            var pendientes = filas.Where(f => despuesDe is null || (f.BearbZeit, f.Etiqueta).CompareTo((despuesDe.Value.Tiempo, despuesDe.Value.Etiqueta)) > 0);
            return Task.FromResult<IReadOnlyList<EscaneoOrigen>>(pendientes.Take(max).ToList());
        }
    }

    private sealed class Registrar(Arnes a, string? falla) : IRequestHandler<RegistrarEscaneoPiezaCommand, EscaneoResultadoDto>
    {
        public Task<EscaneoResultadoDto> Handle(RegistrarEscaneoPiezaCommand r, CancellationToken ct)
        {
            if (r.Etiqueta == falla) throw new InvalidOperationException("falla");
            a.Comandos.Add(r);
            return Task.FromResult(new EscaneoResultadoDto(Guid.NewGuid(), "Aceptado", null, null, false));
        }
    }

    private sealed class Reintentar(Arnes a) : IRequestHandler<ReintentarPospuestosCommand, int>
    {
        public Task<int> Handle(ReintentarPospuestosCommand r, CancellationToken ct) { a.Reintentos.Add(r); return Task.FromResult(0); }
    }

    private sealed class Marca(DateTime? valor) : IRequestHandler<ObtenerMarcaAguaEscaneosQuery, DateTime?>
    {
        public Task<DateTime?> Handle(ObtenerMarcaAguaEscaneosQuery r, CancellationToken ct) => Task.FromResult(valor);
    }

    private sealed class Nada : IDisposable { public void Dispose() { } }
    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => null; public bool IsBypassed => true; public IDisposable Bypass() => new Nada();
    }
    private sealed class FakeOrigen : IAuditOriginContext
    {
        public string? Origin => null; public IDisposable SetOrigin(string origin) => new Nada();
    }
    private sealed class FakeCorrelacion : IAuditCorrelationContext
    {
        public Guid? CorrelationId => null; public IDisposable Begin(Guid correlationId) => new Nada();
    }
    private sealed class Prohibido : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("Un worker apagado no debe resolver ni abrir conexiones.");
    }
}
