using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Administracion.Domain;
using Millet.Compartido.Domain.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Application.TipoCambio;
using Millet.Integraciones.Aw.Infrastructure.TipoCambio.Workers;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Correo;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.UnitTests.TipoCambio;

/// <summary>I13.2 (TI de Millet, 10-nov): aviso por correo cuando el TC de hoy no quedó aplicado en A+W a las 08:00.</summary>
public sealed class AwTipoCambioAvisoCorreoTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 10);
    private static DateTimeOffset A(int h, int m = 0) => new(2026, 11, 10, h, m, 0, TimeSpan.FromHours(-6));

    private readonly Rango _rango = new();
    private readonly Puerto _port = new();
    private readonly Correo _correo = new();
    private readonly string _db = Guid.NewGuid().ToString();
    private readonly Bitacora _log = new();

    private AwTipoCambioWorker CrearWorker(string? correos = AvisoTipoCambioAwParametro.ValorInicial)
    {
        var opts = Options.Create(new AwTipoCambioOptions
        {
            Habilitado = true, HoraLimitePublicacion = "18:00", HoraLimiteAplicacion = "08:00", Monedas = ["USD"],
        });
        var services = new ServiceCollection();
        services.AddSingleton<ITipoCambioRangoReadPort>(_rango);
        services.AddSingleton<IAwTipoCambioPort>(_port);
        services.AddSingleton<ICorreoSaliente>(_correo);
        services.AddSingleton(opts);
        services.AddSingleton<ICurrentEmpresaContext, Empresa>();
        services.AddDbContext<CompartidoDbContext>(o => o.UseInMemoryDatabase(_db));
        services.AddSingleton<ILogger<AwTipoCambioPublicador>>(NullLogger<AwTipoCambioPublicador>.Instance);
        services.AddScoped<AwTipoCambioPublicador>();
        var sp = services.BuildServiceProvider();

        if (correos is not null)
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            db.ParametrosGlobales.Add(ParametroGlobal.CrearDeModulo(AvisoTipoCambioAwParametro.Id,
                AvisoTipoCambioAwParametro.Clave, correos, TipoParametro.Texto, "integraciones.aw", "aviso"));
            db.SaveChanges();
        }

        return new AwTipoCambioWorker(sp.GetRequiredService<IServiceScopeFactory>(), opts,
            _log);
    }

    [Fact]
    public async Task Sin_TC_aplicado_despues_de_las_8_manda_un_solo_correo_al_dia_con_fecha_moneda_valor_y_error()
    {
        _rango.Items.Add(new TipoCambioRangoItem("USD", Hoy, 18.7147m, "Banxico"));
        var worker = CrearWorker();
        _port.NoGuardar = true;

        await worker.EjecutarCicloAsync(A(8, 30));
        await worker.EjecutarCicloAsync(A(9, 30));
        await worker.EjecutarCicloAsync(A(10, 30));

        _correo.Enviados.Should().ContainSingle();
        var m = _correo.Enviados[0];
        m.Para.Should().Equal("soportemillet@millet.com.mx", "contadorgeneral@millet.com.mx", "falejos@millet.com.mx");
        m.Asunto.Should().Contain("2026-11-10").And.Contain("corran la tarea a mano hoy");
        m.Html.Should().Contain("2026-11-10").And.Contain("USD").And.Contain("18.7147")
            .And.Contain("No hay fila publicada").And.Contain("corran la tarea a mano hoy");
    }

    [Fact]
    public async Task Rechazo_de_AW_manda_el_valor_publicado_y_el_motivo()
    {
        _port.Acuses.Add(new AwTipoCambioAcuse("USD", Hoy, 1, 18.7147m, null, 2, "Moneda bloqueada", 3));
        var worker = CrearWorker();

        await worker.EjecutarCicloAsync(A(8, 0));

        _correo.Enviados.Should().ContainSingle()
            .Which.Html.Should().Contain("18.7147").And.Contain("Moneda bloqueada");
    }

    [Fact]
    public async Task Los_destinatarios_salen_del_parametro_editable()
    {
        var worker = CrearWorker("nuevo.ti@millet.com.mx; otra@millet.com.mx");

        await worker.EjecutarCicloAsync(A(8, 15));

        _correo.Enviados.Should().ContainSingle()
            .Which.Para.Should().Equal("nuevo.ti@millet.com.mx", "otra@millet.com.mx");
    }

    [Fact]
    public async Task Parametro_vacio_o_ausente_no_manda_correo()
    {
        await CrearWorker("").EjecutarCicloAsync(A(8, 15));
        _correo.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task TC_aplicado_a_tiempo_no_manda_correo()
    {
        _port.Acuses.Add(new AwTipoCambioAcuse("USD", Hoy, 1, 18.7147m, A(7), 1, null, 1));
        _port.VigenteAw = 0.05343393m;
        var worker = CrearWorker();

        await worker.EjecutarCicloAsync(A(8, 30));

        _correo.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Antes_de_las_8_no_manda_correo()
    {
        var worker = CrearWorker();

        await worker.EjecutarCicloAsync(A(7, 59));

        _correo.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Publicacion_fallida_avisa_solo_despues_de_las_8_y_una_vez()
    {
        _rango.Falla = new InvalidOperationException("Timeout contra SER-DATA");
        var worker = CrearWorker();

        await worker.EjecutarCicloAsync(A(7));
        _correo.Enviados.Should().BeEmpty();

        await worker.EjecutarCicloAsync(A(8));
        await worker.EjecutarCicloAsync(A(9));

        _correo.Enviados.Should().ContainSingle()
            .Which.Html.Should().Contain("Timeout contra SER-DATA").And.Contain("2026-11-10");
    }

    [Fact]
    public async Task Si_el_correo_no_sale_se_reintenta_en_el_siguiente_ciclo()
    {
        var worker = CrearWorker();
        _correo.Resultados.Enqueue(ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.LimiteProveedor, "429"));

        await worker.EjecutarCicloAsync(A(8));
        await worker.EjecutarCicloAsync(A(9));
        await worker.EjecutarCicloAsync(A(10));

        _correo.Enviados.Should().HaveCount(2); // el rechazado y el reintento aceptado; después ya no
    }

    [Fact]
    public async Task Correo_simulado_no_cuenta_como_enviado_avisa_en_el_log_y_se_reintenta()
    {
        var worker = CrearWorker();
        _correo.Resultados.Enqueue(ResultadoCorreoSaliente.Simulado);

        await worker.EjecutarCicloAsync(A(8));
        await worker.EjecutarCicloAsync(A(9));
        await worker.EjecutarCicloAsync(A(10));

        _correo.Enviados.Should().HaveCount(2); // el simulado no se da por avisado; el aceptado sí
        _log.Entradas.Should().ContainSingle(e => e.Nivel == LogLevel.Warning && e.Texto.Contains("simulado"));
    }

    [Theory]
    [InlineData(CodigosCorreoSaliente.PermisosInsuficientes)]
    [InlineData(CodigosCorreoSaliente.ExcedeDestinatarios)]
    [InlineData(CodigosCorreoSaliente.SinDestinatarios)]
    public async Task Error_no_reintentable_se_registra_una_vez_y_no_se_reintenta(string codigo)
    {
        var worker = CrearWorker();
        _correo.Resultados.Enqueue(ResultadoCorreoSaliente.Error(codigo, "rechazado"));

        await worker.EjecutarCicloAsync(A(8));
        await worker.EjecutarCicloAsync(A(9));
        await worker.EjecutarCicloAsync(A(10));

        _correo.Enviados.Should().ContainSingle();
        _log.Entradas.Should().ContainSingle(e => e.Nivel == LogLevel.Error && e.Texto.Contains(codigo));
    }

    [Fact]
    public async Task Falla_de_envio_generica_se_reintenta()
    {
        var worker = CrearWorker();
        _correo.Resultados.Enqueue(ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.ErrorEnvio, "timeout"));

        await worker.EjecutarCicloAsync(A(8));
        await worker.EjecutarCicloAsync(A(9));

        _correo.Enviados.Should().HaveCount(2);
        _log.Entradas.Should().NotContain(e => e.Nivel == LogLevel.Error);
    }

    [Fact]
    public void El_parametro_rechaza_correos_invalidos()
    {
        AvisoTipoCambioAwParametro.LeerCorreos(AvisoTipoCambioAwParametro.ValorInicial).Should().HaveCount(3);
        var p = ParametroGlobal.CrearDeModulo(AvisoTipoCambioAwParametro.Id, AvisoTipoCambioAwParametro.Clave,
            AvisoTipoCambioAwParametro.ValorInicial, TipoParametro.Texto, "integraciones.aw", "aviso");

        var act = () => p.ActualizarValor("soportemillet@millet.com.mx, mmillet");

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("AW_TC_AVISO_CORREO_INVALIDO");
    }

    private sealed class Bitacora : ILogger<AwTipoCambioWorker>
    {
        public List<(LogLevel Nivel, string Texto)> Entradas { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entradas.Add((logLevel, formatter(state, exception)));
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class Correo : ICorreoSaliente
    {
        public List<MensajeCorreo> Enviados { get; } = [];
        public Queue<ResultadoCorreoSaliente> Resultados { get; } = new();
        public bool EsSimulado => false;

        public Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
        {
            Enviados.Add(mensaje);
            return Task.FromResult(Resultados.TryDequeue(out var r) ? r : ResultadoCorreoSaliente.Aceptado);
        }
    }

    private sealed class Rango : ITipoCambioRangoReadPort
    {
        public List<TipoCambioRangoItem> Items { get; } = [];
        public Exception? Falla { get; set; }

        public Task<IReadOnlyList<TipoCambioRangoItem>> ObtenerRangoTipoCambioAsync(
            IReadOnlyList<string> monedas, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
        {
            if (Falla is not null) throw Falla;
            return Task.FromResult<IReadOnlyList<TipoCambioRangoItem>>(
                Items.Where(x => monedas.Contains(x.Moneda) && x.Fecha >= desde && x.Fecha <= hasta).ToList());
        }
    }

    private sealed class Puerto : IAwTipoCambioPort
    {
        public List<AwTipoCambioAcuse> Acuses { get; } = [];
        public decimal? VigenteAw { get; set; }
        public bool NoGuardar { get; set; }

        public Task<int?> PublicarFilaAsync(AwTipoCambioFila fila, CancellationToken ct = default)
        {
            if (!NoGuardar)
                Acuses.Add(new AwTipoCambioAcuse(fila.Moneda, fila.FechaAplicacion, fila.Version, fila.Valor, null, null, null, 0));
            return Task.FromResult<int?>(1);
        }

        public Task<IReadOnlyList<AwTipoCambioAcuse>> ObtenerUltimosAcusesAsync(
            IReadOnlyList<string> monedas, DateOnly desde, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AwTipoCambioAcuse>>(
                Acuses.Where(a => monedas.Contains(a.Moneda) && a.FechaAplicacion >= desde).ToList());

        public Task<decimal?> ObtenerTipoCambioVigenteAwAsync(string moneda, CancellationToken ct = default) =>
            Task.FromResult(VigenteAw);
    }
}
