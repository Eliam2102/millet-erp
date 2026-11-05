using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Users.Item.SendMail;
using Millet.Almacen.Domain.Ports.Notificaciones;
using Millet.Almacen.Infrastructure.Notificaciones;
using Millet.Api.Auth.Options;
using Millet.Api.Correo;
using Millet.CuentasPorCobrar.Domain.Ports.Correo;
using Millet.CuentasPorCobrar.Infrastructure.Adapters;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Correo;
using Millet.SharedKernel.Infrastructure.Correo;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.UnitTests;

/// <summary>86e3txv0n: transporte único de correo saliente, modo pruebas, registro y consumidores.</summary>
public sealed class CorreoSalienteTests
{
    private static MensajeCorreo Mensaje(params string[] para) =>
        new(Guid.NewGuid(), "compras", "documento", Guid.NewGuid(), para, "Orden de compra OC-1", "<p>hola</p>",
            [new AdjuntoCorreo("OC-1.pdf", "application/pdf", [1, 2, 3])]);

    private static readonly ModoPruebasCorreoOptions Pruebas = new()
    {
        Activo = true,
        DominiosPermitidos = ["millet.example", "vilo.example"],
        BuzonesPrueba = ["qa1@vilo.example", "qa2@vilo.example"],
    };

    // ---- Modo pruebas ----

    [Fact]
    public void ModoPruebas_redirige_al_externo_con_asunto_y_destinatario_original()
    {
        var m = ModoPruebasCorreoSaliente.Redirigir(Mensaje("compras@proveedor-real.com"), Pruebas);

        Assert.Equal(["qa1@vilo.example", "qa2@vilo.example"], m.Para);
        Assert.Equal(["compras@proveedor-real.com"], m.ParaOriginal);
        Assert.StartsWith("[PRUEBAS] ", m.Asunto);
        Assert.Contains("Destinatario original: <strong>compras@proveedor-real.com</strong>", m.Html);
        Assert.Single(m.AdjuntosOVacio);
    }

    [Fact]
    public void ModoPruebas_deja_pasar_los_dominios_permitidos_sin_cambios()
    {
        var original = Mensaje("laura@millet.example", "uziel@VILO.EXAMPLE");

        Assert.Same(original, ModoPruebasCorreoSaliente.Redirigir(original, Pruebas));
    }

    [Fact]
    public void ModoPruebas_mixto_conserva_el_permitido_y_cambia_solo_el_externo()
    {
        var m = ModoPruebasCorreoSaliente.Redirigir(Mensaje("laura@millet.example", "cliente@gmail.com"), Pruebas);

        Assert.Equal(["laura@millet.example", "qa1@vilo.example", "qa2@vilo.example"], m.Para);
        Assert.StartsWith("[PRUEBAS] ", m.Asunto);
    }

    [Fact]
    public void ModoPruebas_inactivo_no_toca_el_correo()
    {
        var original = Mensaje("cliente@gmail.com");

        Assert.Same(original, ModoPruebasCorreoSaliente.Redirigir(original, new ModoPruebasCorreoOptions()));
    }

    // ---- Validación al arrancar (criterio 5) ----

    private static readonly EntraIdOptions EntraCompleto = new()
    {
        TenantId = "t", ClientId = "c", ClientSecret = "s", SenderEmail = "",
    };

    [Fact]
    public void Graph_sin_remitente_no_arranca_y_dice_que_falta()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CorreoSalienteRegistration.Validar(
            new CorreoSalienteOptions { Proveedor = "Graph" }, EntraCompleto, isDevelopment: false));

        Assert.Contains("Correo:Remitente", ex.Message);
    }

    [Fact]
    public void Graph_sin_secreto_nombra_la_clave()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CorreoSalienteRegistration.Validar(
            new CorreoSalienteOptions { Proveedor = "Graph", Remitente = "erp@example.com" },
            new EntraIdOptions { TenantId = "t", ClientId = "c" }, isDevelopment: false));

        Assert.Contains("Auth:EntraId:ClientSecret", ex.Message);
        Assert.DoesNotContain("Remitente", ex.Message);
    }

    [Fact]
    public void Graph_completo_arranca()
    {
        CorreoSalienteRegistration.Validar(
            new CorreoSalienteOptions { Proveedor = "Graph", Remitente = "erp@example.com" }, EntraCompleto, isDevelopment: false);
    }

    [Fact]
    public void SandboxLocal_fuera_de_Development_no_arranca()
    {
        Assert.Throws<InvalidOperationException>(() => CorreoSalienteRegistration.Validar(
            new CorreoSalienteOptions { Proveedor = "SandboxLocal" }, new EntraIdOptions(), isDevelopment: false));
    }

    [Fact]
    public void ModoPruebas_sin_buzones_no_arranca()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CorreoSalienteRegistration.Validar(
            new CorreoSalienteOptions { ModoPruebas = new() { Activo = true } }, new EntraIdOptions(), isDevelopment: false));

        Assert.Contains("BuzonesPrueba", ex.Message);
    }

    [Fact]
    public void Proveedor_desconocido_no_arranca()
    {
        Assert.Throws<InvalidOperationException>(() => CorreoSalienteRegistration.Validar(
            new CorreoSalienteOptions { Proveedor = "Smtp" }, new EntraIdOptions(), isDevelopment: true));
    }

    // ---- Graph ----

    [Fact]
    public async Task Graph_manda_desde_el_buzon_del_ERP_con_adjuntos()
    {
        var graph = new GraphFalso();

        var r = await new GraphCorreoSaliente(graph, "erp@example.com", "ERP Millet")
            .EnviarAsync(Mensaje("cliente@example.com"), CancellationToken.None);

        Assert.Equal(EstadoCorreoSaliente.Aceptado, r.Estado);
        Assert.Equal("erp@example.com", graph.Remitente);
        var msg = graph.Cuerpo!.Message!;
        Assert.Equal("ERP Millet", msg.From!.EmailAddress!.Name);
        Assert.Equal("cliente@example.com", Assert.Single(msg.ToRecipients!).EmailAddress!.Address);
        var adjunto = Assert.IsType<FileAttachment>(Assert.Single(msg.Attachments!));
        Assert.Equal("OC-1.pdf", adjunto.Name);
        Assert.Equal(new byte[] { 1, 2, 3 }, adjunto.ContentBytes);
    }

    [Fact]
    public async Task Graph_permisos_insuficientes_devuelve_error_con_motivo()
    {
        var graph = new GraphFalso { Error = new ODataError { ResponseStatusCode = 403, Error = new MainError { Code = "ErrorAccessDenied", Message = "Access is denied." } } };

        var r = await new GraphCorreoSaliente(graph, "erp@example.com", "ERP Millet")
            .EnviarAsync(Mensaje("cliente@example.com"), CancellationToken.None);

        Assert.Equal(EstadoCorreoSaliente.Error, r.Estado);
        Assert.Equal(CodigosCorreoSaliente.PermisosInsuficientes, r.Codigo);
        Assert.Contains("Mail.Send", r.Motivo);
    }

    [Fact]
    public async Task Graph_adjuntos_de_mas_de_3MB_no_se_mandan()
    {
        var graph = new GraphFalso();
        var grande = Mensaje("cliente@example.com") with
        {
            Adjuntos = [new AdjuntoCorreo("grande.pdf", "application/pdf", new byte[3 * 1024 * 1024 + 1])],
        };

        var r = await new GraphCorreoSaliente(graph, "erp@example.com", "ERP Millet").EnviarAsync(grande, CancellationToken.None);

        Assert.Equal(CodigosCorreoSaliente.AdjuntosExceden3Mb, r.Codigo);
        Assert.Null(graph.Cuerpo);
    }

    // ---- Registro ----

    [Fact]
    public async Task Registro_guarda_aceptado_con_destinatario_original_y_real()
    {
        using var provider = ConCore();
        var registro = Registro(new CorreoSalienteEnMemoria(), provider);
        var correo = new ModoPruebasCorreoSaliente(registro, Pruebas);

        var r = await correo.EnviarAsync(Mensaje("compras@proveedor-real.com"), CancellationToken.None);

        Assert.Equal(EstadoCorreoSaliente.Aceptado, r.Estado);
        var fila = Assert.Single(await LeerAsync(provider));
        Assert.Equal("aceptado", fila.Estado);
        Assert.Equal("compras@proveedor-real.com", fila.ParaOriginal);
        Assert.Equal("qa1@vilo.example, qa2@vilo.example", fila.Para);
        Assert.Equal("compras", fila.Modulo);
        Assert.Equal(1, fila.Adjuntos);
        Assert.Equal(3, fila.BytesAdjuntos);
    }

    [Fact]
    public async Task Registro_convierte_la_excepcion_del_transporte_en_error_registrado()
    {
        using var provider = ConCore();
        var registro = Registro(new TransporteQueLanza(), provider);

        var r = await registro.EnviarAsync(Mensaje("cliente@example.com"), CancellationToken.None);

        Assert.Equal(EstadoCorreoSaliente.Error, r.Estado);
        var fila = Assert.Single(await LeerAsync(provider));
        Assert.Equal("error", fila.Estado);
        Assert.Equal(CodigosCorreoSaliente.ErrorEnvio, fila.Codigo);
        Assert.Equal("se cayó la red", fila.Motivo);
    }

    [Fact]
    public async Task Registro_sin_destinatarios_no_llama_al_transporte()
    {
        using var provider = ConCore();
        var transporte = new CorreoSalienteEnMemoria();

        var r = await Registro(transporte, provider).EnviarAsync(Mensaje(), CancellationToken.None);

        Assert.Equal(CodigosCorreoSaliente.SinDestinatarios, r.Codigo);
        Assert.Empty(transporte.Enviados);
    }

    // ---- Consumidores ----

    [Fact]
    public async Task Documento_OC_lleva_el_PDF_y_EsSimulado_sigue_al_proveedor()
    {
        var transporte = new CorreoSalienteEnMemoria();
        var servicio = new CorreoDocumentoService(transporte);
        var ocId = Guid.NewGuid();

        await servicio.EnviarAsync(new CorreoDocumento(Guid.NewGuid(), "ventas@proveedor.com", "Orden de compra OC-1",
            "OC-1.pdf", [9], Modulo: "compras", ReferenciaId: ocId), CancellationToken.None);

        Assert.False(servicio.EsSimulado);
        var m = Assert.Single(transporte.Enviados);
        Assert.Equal(ocId, m.ReferenciaId);
        Assert.Equal("OC-1.pdf", Assert.Single(m.AdjuntosOVacio).Nombre);
        Assert.True(new CorreoDocumentoService(new CorreoSalienteEnMemoria { EsSimulado = true }).EsSimulado);
    }

    [Fact]
    public async Task Documento_rechazado_lanza_para_que_la_OC_quede_fallida()
    {
        var servicio = new CorreoDocumentoService(new CorreoSalienteEnMemoria
        {
            FallarCon = ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.ErrorEnvio, "Graph caído"),
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.EnviarAsync(
            new CorreoDocumento(Guid.NewGuid(), "v@p.com", "OC", "OC.pdf", [1]), CancellationToken.None));
    }

    [Fact]
    public async Task EstadoCuenta_aceptado_es_true_simulado_es_false_y_error_lanza()
    {
        var correo = new CorreoEstadoCuenta("cliente@example.com", "Cliente SA", null, "edo.pdf", [1]);

        Assert.True(await new CorreoSalienteEstadoCuentaAdapter(new CorreoSalienteEnMemoria()).EnviarAsync(correo, CancellationToken.None));
        Assert.False(await new CorreoSalienteEstadoCuentaAdapter(new CorreoSalienteEnMemoria { EsSimulado = true })
            .EnviarAsync(correo, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CorreoSalienteEstadoCuentaAdapter(new CorreoSalienteEnMemoria
        {
            FallarCon = ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.ErrorEnvio, "x"),
        }).EnviarAsync(correo, CancellationToken.None));
    }

    [Fact]
    public async Task Almacen_avisa_a_Finanzas_y_el_vale_vencido_al_Jefe_de_almacen()
    {
        var transporte = new CorreoSalienteEnMemoria();
        var servicio = new CorreoNotificacionService(transporte, Options.Create(new AvisosAlmacenOptions
        {
            Finanzas = ["finanzas@millet.example", "no-es-correo"],
            JefeAlmacen = ["jefe@millet.example"],
        }), NullLogger<CorreoNotificacionService>.Instance);

        await servicio.NotificarAjusteNivel3AprobadoAsync(new AvisoAjusteNivel3(Guid.NewGuid(), Guid.NewGuid(), 12500m,
            Guid.NewGuid(), DateTimeOffset.UtcNow, [new ConteoAlmacenSucursal(Guid.NewGuid(), Guid.NewGuid())]), CancellationToken.None);
        await servicio.NotificarValeSinRegularizarAsync(Guid.NewGuid(), "VU-0007", null, DateTimeOffset.UtcNow, 2, CancellationToken.None);

        Assert.Collection(transporte.Enviados,
            m => { Assert.Equal(["finanzas@millet.example"], m.Para); Assert.Equal("ajuste-nivel-3", m.Tipo); },
            m => { Assert.Equal(["jefe@millet.example"], m.Para); Assert.Contains("VU-0007", m.Asunto); });
    }

    [Fact]
    public async Task Almacen_sin_correos_configurados_no_manda_nada()
    {
        var transporte = new CorreoSalienteEnMemoria();
        var servicio = new CorreoNotificacionService(transporte, Options.Create(new AvisosAlmacenOptions()),
            NullLogger<CorreoNotificacionService>.Instance);

        await servicio.NotificarValeSinRegularizarAsync(Guid.NewGuid(), "VU-1", null, DateTimeOffset.UtcNow, 1, CancellationToken.None);

        Assert.Empty(transporte.Enviados);
    }

    // ---- Dobles ----

    private static ServiceProvider ConCore()
    {
        var nombre = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext, Empresa>();
        services.AddDbContext<CoreDbContext>(o => o.UseInMemoryDatabase(nombre));
        return services.BuildServiceProvider();
    }

    private static RegistroCorreoSaliente Registro(ICorreoSaliente interno, IServiceProvider provider) =>
        new(interno, "Graph", provider.GetRequiredService<IServiceScopeFactory>(), new Reloj(),
            NullLogger<RegistroCorreoSaliente>.Instance);

    private static async Task<List<CorreoEnvio>> LeerAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CoreDbContext>().CorreoEnvios.AsNoTracking().ToListAsync();
    }

    private sealed class GraphFalso : IGraphSendMail
    {
        public ODataError? Error { get; init; }
        public string? Remitente { get; private set; }
        public SendMailPostRequestBody? Cuerpo { get; private set; }

        public Task EnviarAsync(string remitente, SendMailPostRequestBody cuerpo, CancellationToken cancellationToken)
        {
            if (Error is not null) throw Error;
            Remitente = remitente;
            Cuerpo = cuerpo;
            return Task.CompletedTask;
        }
    }

    private sealed class TransporteQueLanza : ICorreoSaliente
    {
        public bool EsSimulado => false;
        public Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken) =>
            throw new HttpRequestException("se cayó la red");
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => throw new NotSupportedException();
    }

    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
}
