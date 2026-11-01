using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Application.Monedas;
using Millet.Compartido.Application.Ports;
using Millet.Compartido.Infrastructure.Banxico;
using Millet.SharedKernel.Application;
using Xunit;

namespace Millet.Compartido.UnitTests.Catalogos;

/// <summary>K10.6: lectura de la respuesta del SIE y avisos del worker. Datos ficticios.</summary>
public sealed class TipoCambioBanxicoWorkerTests
{
    private const string RespuestaSie = """
        {"bmx":{"series":[
          {"idSerie":"SF43718","titulo":"FIX","datos":[{"fecha":"20/10/2026","dato":"18.4200"},{"fecha":"21/10/2026","dato":"18.6425"}]},
          {"idSerie":"SF46410","titulo":"EUR","datos":[{"fecha":"20/10/2026","dato":"N/E"},{"fecha":"21/10/2026","dato":"20.9717"}]},
          {"idSerie":"SF60632","titulo":"CAD"}
        ]}}
        """;

    [Fact]
    public void Lee_las_series_por_fecha_y_omite_los_dias_sin_dato()
    {
        using var json = JsonDocument.Parse(RespuestaSie);

        var series = BanxicoSieHttpAdapter.Leer(json.RootElement);

        series[SeriesBanxico.FixUsd].Should().Equal(new Dictionary<DateOnly, decimal>
        {
            [new DateOnly(2026, 10, 20)] = 18.4200m, [new DateOnly(2026, 10, 21)] = 18.6425m,
        });
        series[SeriesBanxico.Eur].Should().Equal(new Dictionary<DateOnly, decimal> { [new DateOnly(2026, 10, 21)] = 20.9717m });
        series[SeriesBanxico.Cad].Should().BeEmpty();
    }

    [Fact]
    public async Task Pide_las_series_del_rango_con_el_token_en_el_encabezado()
    {
        var handler = new HandlerFalso(RespuestaSie);
        var adapter = new BanxicoSieHttpAdapter(new ClientesFalsos(handler),
            Options.Create(new BanxicoOptions { Token = "token-ficticio" }));

        var series = await adapter.ObtenerSeriesAsync([SeriesBanxico.FixUsd, SeriesBanxico.Eur],
            new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 22), CancellationToken.None);

        handler.Solicitud!.RequestUri!.ToString().Should()
            .Be("https://sie.example/series/SF43718,SF46410/datos/2026-10-07/2026-10-22");
        handler.Solicitud.Headers.GetValues("Bmx-Token").Should().Equal("token-ficticio");
        series.Should().ContainKey(SeriesBanxico.FixUsd);
    }

    [Fact]
    public async Task Si_Banxico_falla_avisa_al_Contador_General_una_sola_vez_al_dia()
    {
        var escenario = new Escenario { Falla = new HttpRequestException("Banxico no responde") };

        await escenario.Worker.CorrerUnaVezAsync(CancellationToken.None);
        await escenario.Worker.CorrerUnaVezAsync(CancellationToken.None);

        var aviso = escenario.Alertas.Recibidas.Should().ContainSingle().Subject;
        aviso.Rol.Should().Be("Contador General");
        aviso.Asunto.Should().Contain("No se pudo descargar");
        aviso.Detalle.Should().Contain("Banxico no responde").And.Contain("capture el TC a mano");

        escenario.Reloj.UtcNow = escenario.Reloj.UtcNow.AddDays(1);
        await escenario.Worker.CorrerUnaVezAsync(CancellationToken.None);
        escenario.Alertas.Recibidas.Should().HaveCount(2);
    }

    [Fact]
    public async Task Avisa_las_capturas_manuales_distintas_y_no_avisa_si_todo_cuadra()
    {
        var sinConflictos = new Escenario { Resultado = Resultado() };
        await sinConflictos.Worker.CorrerUnaVezAsync(CancellationToken.None);
        sinConflictos.Alertas.Recibidas.Should().BeEmpty();

        var conConflictos = new Escenario { Resultado = Resultado([new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 22)]) };
        await conConflictos.Worker.CorrerUnaVezAsync(CancellationToken.None);
        await conConflictos.Worker.CorrerUnaVezAsync(CancellationToken.None);

        conConflictos.Alertas.Recibidas.Should().ContainSingle()
            .Which.Detalle.Should().Be("Se conservó la captura manual en: USD 21/10/2026, 22/10/2026.");
    }

    [Fact]
    public async Task Sin_token_el_worker_no_descarga()
    {
        var escenario = new Escenario { Resultado = Resultado(), Token = null };

        await escenario.Worker.StartAsync(CancellationToken.None);
        await escenario.Worker.ExecuteTask!;

        escenario.Envios.Should().Be(0);
    }

    private static DescargaTipoCambioResultado Resultado(IReadOnlyList<DateOnly>? conflictosUsd = null) =>
        new(new DateOnly(2026, 10, 22),
        [
            new DescargaTipoCambioMoneda("USD", 1, 0, conflictosUsd ?? [], new DateOnly(2026, 10, 23)),
            new DescargaTipoCambioMoneda("EUR", 1, 0, [], new DateOnly(2026, 10, 23)),
        ]);

    private sealed class Escenario
    {
        public Exception? Falla { get; init; }
        public DescargaTipoCambioResultado? Resultado { get; init; }
        public string? Token { get; init; } = "token-ficticio";
        public AlertasFalsas Alertas { get; } = new();
        public RelojFalso Reloj { get; } = new();
        public int Envios => _sender.Envios;

        private SenderFalso _sender = null!;
        private TipoCambioBanxicoWorker? _worker;

        public TipoCambioBanxicoWorker Worker => _worker ??= Crear();

        private TipoCambioBanxicoWorker Crear()
        {
            _sender = new SenderFalso(this);
            var servicios = new ServiceCollection()
                .AddSingleton<ISender>(_sender)
                .AddSingleton<IAlertaTipoCambioPort>(Alertas)
                .BuildServiceProvider();
            return new TipoCambioBanxicoWorker(servicios.GetRequiredService<IServiceScopeFactory>(), Reloj,
                Options.Create(new BanxicoOptions { Token = Token }), NullLogger<TipoCambioBanxicoWorker>.Instance);
        }

        public DescargaTipoCambioResultado Descargar() => Falla is not null ? throw Falla : Resultado!;
    }

    private sealed class SenderFalso(Escenario escenario) : ISender
    {
        public int Envios { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Envios++;
            return Task.FromResult((TResponse)(object)escenario.Descargar());
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class AlertasFalsas : IAlertaTipoCambioPort
    {
        public List<AlertaTipoCambio> Recibidas { get; } = [];

        public Task NotificarAsync(AlertaTipoCambio alerta, CancellationToken cancellationToken)
        {
            Recibidas.Add(alerta);
            return Task.CompletedTask;
        }
    }

    private sealed class RelojFalso : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 22, 18, 30, 0, TimeSpan.Zero);
    }

    private sealed class HandlerFalso(string cuerpo) : HttpMessageHandler
    {
        public HttpRequestMessage? Solicitud { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Solicitud = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ClientesFalsos(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://sie.example/") };
    }
}
