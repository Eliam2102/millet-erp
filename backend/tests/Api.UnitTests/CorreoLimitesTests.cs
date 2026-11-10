using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Users.Item.SendMail;
using Millet.Api.Auth.Options;
using Millet.Api.Correo;
using Millet.SharedKernel.Application.Correo;
using Millet.SharedKernel.Infrastructure.Correo;

namespace Millet.Api.UnitTests;

/// <summary>86e3zr74h: límites de Exchange Online, reintento ante limitación y aviso de vencimiento del secreto.</summary>
public sealed class CorreoLimitesTests
{
    private static MensajeCorreo Mensaje(int destinatarios = 1) =>
        new(null, "compras", "documento", Guid.NewGuid(),
            [.. Enumerable.Range(1, destinatarios).Select(i => $"d{i}@example.com")], "Asunto", "<p>x</p>");

    private static LimitesCorreoOptions Limites(int porMinuto = 20, int porMensaje = 300, int porDia = 8000, int reintentos = 3) => new()
    {
        MensajesPorMinuto = porMinuto,
        MaxDestinatariosPorMensaje = porMensaje,
        MaxDestinatariosPorDia = porDia,
        Reintentos = reintentos,
        EsperaInicialSegundos = 10,
    };

    // ---- Control de volumen ----

    [Fact]
    public async Task Al_llegar_al_tope_por_minuto_espera_su_turno_en_lugar_de_fallar()
    {
        var reloj = new RelojManual();
        var transporte = new TransporteFalso();
        var limites = new LimitesCorreoSaliente(transporte, Limites(porMinuto: 2), reloj, reloj.EsperarAsync);

        for (var i = 0; i < 3; i++)
            Assert.True((await limites.EnviarAsync(Mensaje(), CancellationToken.None)).FueAceptado);

        Assert.Equal(3, transporte.Envios);
        Assert.Equal([TimeSpan.FromMinutes(1)], reloj.Esperas);
    }

    [Fact]
    public async Task Mensaje_con_mas_destinatarios_del_maximo_se_rechaza_sin_enviarse()
    {
        var transporte = new TransporteFalso();
        var limites = new LimitesCorreoSaliente(transporte, Limites(porMensaje: 3), new RelojManual());

        var r = await limites.EnviarAsync(Mensaje(4), CancellationToken.None);

        Assert.Equal(CodigosCorreoSaliente.ExcedeDestinatarios, r.Codigo);
        Assert.False(CodigosCorreoSaliente.EsReintentable(r.Codigo));
        Assert.Equal(0, transporte.Envios);
    }

    [Fact]
    public async Task Tope_diario_devuelve_error_reintentable_y_se_libera_a_las_24_horas()
    {
        var reloj = new RelojManual();
        var transporte = new TransporteFalso();
        var limites = new LimitesCorreoSaliente(transporte, Limites(porDia: 5), reloj, reloj.EsperarAsync);

        Assert.True((await limites.EnviarAsync(Mensaje(3), CancellationToken.None)).FueAceptado);
        var lleno = await limites.EnviarAsync(Mensaje(3), CancellationToken.None);
        Assert.Equal(CodigosCorreoSaliente.LimiteDiario, lleno.Codigo);
        Assert.True(CodigosCorreoSaliente.EsReintentable(lleno.Codigo));
        Assert.Equal(1, transporte.Envios);

        reloj.Avanzar(TimeSpan.FromHours(24));
        Assert.True((await limites.EnviarAsync(Mensaje(3), CancellationToken.None)).FueAceptado);
    }

    // ---- Reintento ante limitación ----

    [Fact]
    public async Task Limitacion_del_proveedor_se_reintenta_con_espera_creciente_hasta_aceptar()
    {
        var reloj = new RelojManual();
        var transporte = new TransporteFalso { FallasDeLimite = 2 };
        var limites = new LimitesCorreoSaliente(transporte, Limites(), reloj, reloj.EsperarAsync);

        var r = await limites.EnviarAsync(Mensaje(), CancellationToken.None);

        Assert.True(r.FueAceptado);
        Assert.Equal(3, transporte.Envios);
        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)], reloj.Esperas);
    }

    [Fact]
    public async Task Reintentos_agotados_devuelven_el_error_reintentable_para_que_el_modulo_no_pierda_el_correo()
    {
        var reloj = new RelojManual();
        var transporte = new TransporteFalso { FallasDeLimite = 10 };
        var limites = new LimitesCorreoSaliente(transporte, Limites(reintentos: 2), reloj, reloj.EsperarAsync);

        var r = await limites.EnviarAsync(Mensaje(), CancellationToken.None);

        Assert.Equal(CodigosCorreoSaliente.LimiteProveedor, r.Codigo);
        Assert.True(CodigosCorreoSaliente.EsReintentable(r.Codigo));
        Assert.Equal(3, transporte.Envios);
    }

    [Fact]
    public async Task Error_que_no_es_limitacion_no_se_reintenta()
    {
        var transporte = new TransporteFalso { Resultado = ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.PermisosInsuficientes, "x") };
        var limites = new LimitesCorreoSaliente(transporte, Limites(), new RelojManual());

        var r = await limites.EnviarAsync(Mensaje(), CancellationToken.None);

        Assert.Equal(CodigosCorreoSaliente.PermisosInsuficientes, r.Codigo);
        Assert.Equal(1, transporte.Envios);
    }

    // ---- Clasificación de errores de Graph ----

    [Theory]
    [InlineData(429, null, "Too many requests")]
    [InlineData(503, null, "Service unavailable")]
    [InlineData(400, "ErrorExceededMessageLimit", "Message limit exceeded")]
    [InlineData(400, "ErrorSendAsDenied", "550 5.7.233 Remote server returned 'external recipient limit reached'")]
    [InlineData(400, "MailboxConcurrency", "Application is over its MailboxConcurrency limit.")]
    public async Task Graph_clasifica_la_limitacion_como_reintentable(int status, string? codigo, string mensaje)
    {
        var graph = new GraphQueLanza(new ODataError { ResponseStatusCode = status, Error = new MainError { Code = codigo, Message = mensaje } });

        var r = await new GraphCorreoSaliente(graph, "erp@example.com", "ERP Millet").EnviarAsync(Mensaje(), CancellationToken.None);

        Assert.Equal(CodigosCorreoSaliente.LimiteProveedor, r.Codigo);
    }

    [Fact]
    public async Task Graph_error_comun_no_es_limitacion()
    {
        var graph = new GraphQueLanza(new ODataError { ResponseStatusCode = 400, Error = new MainError { Code = "ErrorInvalidRecipients", Message = "Invalid recipient" } });

        var r = await new GraphCorreoSaliente(graph, "erp@example.com", "ERP Millet").EnviarAsync(Mensaje(), CancellationToken.None);

        Assert.Equal(CodigosCorreoSaliente.ErrorEnvio, r.Codigo);
        Assert.False(CodigosCorreoSaliente.EsReintentable(r.Codigo));
    }

    // ---- Configuración ----

    [Theory]
    [InlineData(31, 300, 8000)]
    [InlineData(20, 501, 8000)]
    [InlineData(20, 300, 10_001)]
    [InlineData(0, 300, 8000)]
    public void Limites_arriba_de_Exchange_no_arrancan(int porMinuto, int porMensaje, int porDia)
    {
        var opciones = new CorreoSalienteOptions { Limites = Limites(porMinuto, porMensaje, porDia) };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CorreoSalienteRegistration.Validar(opciones, new EntraIdOptions(), isDevelopment: false));
        Assert.Contains("Correo:Limites", ex.Message);
    }

    [Fact]
    public void Limites_por_omision_quedan_abajo_de_Exchange()
    {
        var l = new CorreoSalienteOptions().Limites;
        Assert.InRange(l.MensajesPorMinuto, 1, 29);
        Assert.InRange(l.MaxDestinatariosPorMensaje, 1, 499);
        Assert.InRange(l.MaxDestinatariosPorDia, 1, 9_999);
        CorreoSalienteRegistration.Validar(new CorreoSalienteOptions(), new EntraIdOptions(), isDevelopment: false);
    }

    // ---- Vencimiento del secreto ----

    [Fact]
    public void Secreto_vigente_lejos_del_vencimiento_esta_sano()
    {
        var v = new VencimientoSecretoCorreo(new DateTimeOffset(2027, 1, 27, 0, 0, 0, TimeSpan.FromHours(-6)), TimeSpan.FromDays(30));
        Assert.Equal(HealthStatus.Healthy, v.Evaluar(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero)).Status);
    }

    [Fact]
    public void Secreto_dentro_de_la_ventana_avisa_con_la_fecha()
    {
        var v = new VencimientoSecretoCorreo(new DateTimeOffset(2027, 1, 27, 0, 0, 0, TimeSpan.FromHours(-6)), TimeSpan.FromDays(30));
        var r = v.Evaluar(new DateTimeOffset(2027, 1, 10, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(HealthStatus.Degraded, r.Status);
        Assert.Contains("2027-01-27", r.Description);
    }

    [Fact]
    public void Secreto_vencido_no_esta_sano()
    {
        var v = new VencimientoSecretoCorreo(new DateTimeOffset(2027, 1, 27, 0, 0, 0, TimeSpan.FromHours(-6)), TimeSpan.FromDays(30));
        Assert.Equal(HealthStatus.Unhealthy, v.Evaluar(new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero)).Status);
    }

    // ---- Dobles ----

    private sealed class RelojManual : TimeProvider
    {
        private DateTimeOffset _ahora = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        public List<TimeSpan> Esperas { get; } = [];
        public override DateTimeOffset GetUtcNow() => _ahora;
        public void Avanzar(TimeSpan t) => _ahora += t;

        public Task EsperarAsync(TimeSpan t, CancellationToken _)
        {
            Esperas.Add(t);
            Avanzar(t);
            return Task.CompletedTask;
        }
    }

    private sealed class TransporteFalso : ICorreoSaliente
    {
        public int FallasDeLimite { get; set; }
        public ResultadoCorreoSaliente Resultado { get; init; } = ResultadoCorreoSaliente.Aceptado;
        public int Envios { get; private set; }
        public bool EsSimulado => false;

        public Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
        {
            Envios++;
            if (FallasDeLimite-- > 0)
                return Task.FromResult(ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.LimiteProveedor, "429"));
            return Task.FromResult(Resultado);
        }
    }

    private sealed class GraphQueLanza(ODataError error) : IGraphSendMail
    {
        public Task EnviarAsync(string remitente, SendMailPostRequestBody cuerpo, CancellationToken cancellationToken) => throw error;
    }
}
