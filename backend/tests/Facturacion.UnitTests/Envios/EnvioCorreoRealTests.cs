using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Facturas.ReenviarCfdiCorreo;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Envios;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Notificaciones;
using Millet.Facturacion.Infrastructure.Pdf;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Correo;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Infrastructure;
using Millet.SharedKernel.Infrastructure.Correo;

namespace Millet.Facturacion.UnitTests.Envios;

/// <summary>86e3txv0n: el CFDI al cliente sale por el transporte único de correo.</summary>
public sealed class EnvioCorreoRealTests
{
    private static EnvioCfdiCorreo Envio(byte[]? pdf = null, string? xml = "<cfdi/>") =>
        new(Guid.NewGuid(), Guid.NewGuid(), "cliente@example.com", "FA-000001", "UUID-1", pdf ?? [1, 2, 3], xml);

    [Fact]
    public async Task CorreoCfdi_manda_pdf_y_xml_al_cliente_por_el_transporte()
    {
        var transporte = new CorreoSalienteEnMemoria();
        var envio = Envio();

        var aceptado = await new CorreoCfdiNotificacionService(transporte).EnviarCfdiPorCorreoAsync(envio, CancellationToken.None);

        aceptado.Should().BeTrue();
        var m = transporte.Enviados.Should().ContainSingle().Subject;
        m.Para.Should().Equal("cliente@example.com");
        m.Modulo.Should().Be("facturacion");
        m.ReferenciaId.Should().Be(envio.ComprobanteId);
        m.Asunto.Should().Contain("FA-000001");
        m.AdjuntosOVacio.Select(a => (a.Nombre, a.ContentType)).Should().Equal(
            ("FA-000001_UUID-1.pdf", "application/pdf"), ("FA-000001_UUID-1.xml", "application/xml"));
        Encoding.UTF8.GetString(m.AdjuntosOVacio[1].Contenido).Should().Be("<cfdi/>");
    }

    [Fact]
    public async Task CorreoCfdi_rechazado_lanza_con_codigo_y_motivo()
    {
        var transporte = new CorreoSalienteEnMemoria
        {
            FallarCon = ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.PermisosInsuficientes, "falta Mail.Send"),
        };

        var act = () => new CorreoCfdiNotificacionService(transporte).EnviarCfdiPorCorreoAsync(Envio(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("CORREO_PERMISOS_INSUFICIENTES: falta Mail.Send");
    }

    [Fact]
    public async Task CorreoCfdi_con_proveedor_NoOp_se_da_por_aceptado_como_antes()
    {
        var transporte = new CorreoSalienteEnMemoria { EsSimulado = true };

        (await new CorreoCfdiNotificacionService(transporte).EnviarCfdiPorCorreoAsync(Envio(), CancellationToken.None))
            .Should().BeTrue();
    }

    [Fact]
    public void Bitacora_Reencolar_vuelve_a_Pendiente_y_no_aplica_si_ya_salio()
    {
        var b = BitacoraEnvioCorreo.Encolar(Guid.NewGuid(), Guid.NewGuid(), "c@example.com");
        b.MarcarFallido("x");

        b.Reencolar();

        b.Estado.Should().Be(EstadoEnvioCorreo.Pendiente);
        b.MarcarEnviado(DateTimeOffset.UtcNow);
        b.Invoking(x => x.Reencolar()).Should().Throw<BusinessRuleException>().Where(e => e.Code == "ENVIO_YA_ENVIADO");
    }

    /// <summary>
    /// Criterio 2 de la ficha: Graph rechaza → la bitácora queda Fallido con el motivo y la
    /// factura no se marca enviada; el reenvío reusa la misma fila y sale un solo correo.
    /// </summary>
    [Fact]
    public async Task Envio_rechazado_queda_Fallido_con_motivo_y_el_reenvio_manda_un_solo_correo()
    {
        var empresaId = Guid.NewGuid();
        var transporte = new CorreoSalienteEnMemoria
        {
            FallarCon = ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.PermisosInsuficientes, "falta Mail.Send"),
        };
        using var provider = Servicios(empresaId, transporte);
        var facturaId = await SembrarFacturaAsync(provider, empresaId);
        var worker = new EnvioCfdiCorreoWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            new OpcionesFijas(new EnvioCfdiCorreoOptions { MaxIntentos = 1 }), NullLogger<EnvioCfdiCorreoWorker>.Instance);

        await ReenviarAsync(provider, empresaId, facturaId);
        (await worker.TickAsync(CancellationToken.None)).Should().Be(0);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var b = await db.BitacorasEnvioCorreo.SingleAsync();
            b.Estado.Should().Be(EstadoEnvioCorreo.Fallido);
            b.UltimoError.Should().Contain("CORREO_PERMISOS_INSUFICIENTES").And.Contain("falta Mail.Send");
            (await db.FacturasVenta.SingleAsync()).EnviadoCorreo.Should().BeFalse();
        }

        // Se agotaron los reintentos automáticos: el siguiente ciclo no lo toca.
        transporte.FallarCon = null;
        (await worker.TickAsync(CancellationToken.None)).Should().Be(0);

        await ReenviarAsync(provider, empresaId, facturaId);
        (await worker.TickAsync(CancellationToken.None)).Should().Be(1);
        (await worker.TickAsync(CancellationToken.None)).Should().Be(0);

        transporte.Enviados.Should().ContainSingle();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var b = await db.BitacorasEnvioCorreo.SingleAsync();
            b.Estado.Should().Be(EstadoEnvioCorreo.Enviado);
            (await db.FacturasVenta.SingleAsync()).EnviadoCorreo.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Reenvio_despues_de_un_envio_exitoso_crea_otro()
    {
        var empresaId = Guid.NewGuid();
        using var provider = Servicios(empresaId, new CorreoSalienteEnMemoria());
        var facturaId = await SembrarFacturaAsync(provider, empresaId);
        var worker = new EnvioCfdiCorreoWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            new OpcionesFijas(new EnvioCfdiCorreoOptions()), NullLogger<EnvioCfdiCorreoWorker>.Instance);

        await ReenviarAsync(provider, empresaId, facturaId);
        await worker.TickAsync(CancellationToken.None);
        await ReenviarAsync(provider, empresaId, facturaId);

        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<FacturacionDbContext>().BitacorasEnvioCorreo.CountAsync())
            .Should().Be(2);
    }

    private static ServiceProvider Servicios(Guid empresaId, ICorreoSaliente transporte)
    {
        var nombreDb = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(empresaId));
        services.AddDbContext<FacturacionDbContext>(o => o.UseInMemoryDatabase(nombreDb));
        services.AddSingleton<IGenerarPdfFacturaPort, QuestPdfFacturaGenerator>();
        services.AddSingleton<IDatosImpresionCfdiReadPort>(new FakeDatosImpresionCfdi());
        services.AddSingleton<ICfdiRepositorioPort>(new FakeCfdiRepositorioPort());
        services.AddSingleton<IClock>(new FakeClock(DateTimeOffset.UtcNow));
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddSingleton(transporte);
        services.AddSingleton<INotificacionService, CorreoCfdiNotificacionService>();
        return services.BuildServiceProvider();
    }

    private static async Task<Guid> SembrarFacturaAsync(IServiceProvider provider, Guid empresaId)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var f = FacturaVenta.CrearBorrador(
            empresaId, "FA-000001", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("XAXX010101000", "Público", "616", "97000", "S01", "MEX", true),
            new DatosFiscalesEmisor("AAA010101AAA", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "01010101", "Producto de prueba", "H87", 2m, 100m, 0m, "02", 0.16m, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado("UUID-1", "sc", "ss", "20001000000300022815", DateTimeOffset.UtcNow, "SAT970701NN3", Guid.NewGuid());
        db.FacturasVenta.Add(f);
        await db.SaveChangesAsync();
        return f.Id;
    }

    private static async Task ReenviarAsync(IServiceProvider provider, Guid empresaId, Guid facturaId)
    {
        using var scope = provider.CreateScope();
        var handler = new ReenviarCfdiCorreoHandler(
            scope.ServiceProvider.GetRequiredService<FacturacionDbContext>(), new FakeEmpresaContext(empresaId));
        await handler.Handle(new ReenviarCfdiCorreoCommand(facturaId, "cliente@example.com"), CancellationToken.None);
    }

    private sealed class OpcionesFijas(EnvioCfdiCorreoOptions valor) : IOptionsMonitor<EnvioCfdiCorreoOptions>
    {
        public EnvioCfdiCorreoOptions CurrentValue => valor;
        public EnvioCfdiCorreoOptions Get(string? name) => valor;
        public IDisposable? OnChange(Action<EnvioCfdiCorreoOptions, string?> listener) => null;
    }
}
