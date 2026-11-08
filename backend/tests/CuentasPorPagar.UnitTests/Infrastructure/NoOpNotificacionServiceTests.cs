using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Domain.Notificaciones;
using Millet.CuentasPorPagar.Infrastructure;
using Millet.CuentasPorPagar.Infrastructure.Notificaciones;
using Millet.SharedKernel.Application.Correo;
using Xunit;

namespace Millet.CuentasPorPagar.UnitTests.Infrastructure;

public sealed class NoOpNotificacionServiceTests
{
    private static readonly Guid FacturaId = Guid.NewGuid();
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly SlaRevisionNotificacion Notificacion = new(
        EmpresaId: EmpresaId,
        FacturaProveedorId: FacturaId,
        DependenciaRevisoraId: Guid.NewGuid(),
        MotivoRevisionId: Guid.NewGuid(),
        DiasEnRevision: 4,
        SlaDias: 3,
        Nivel: NivelEscalamientoSla.SlaVencido,
        Folio: "FAC-1234",
        MotivoNombre: "Diferencia de precio",
        AvisaCompras: true);

    [Fact]
    public async Task Cuando_Proveedor_es_Graph_y_correo_real_disponible_delega_en_transporte()
    {
        var opciones = Options.Create(new AvisosRevisionOptions
        {
            Proveedor = "Graph",
            ContadorGeneral = ["contador@millet.com.mx"],
            Compras = ["compras@millet.com.mx"]
        });
        var correoFake = new FakeCorreoSaliente(esSimulado: false);
        var sut = new NoOpNotificacionService(
            NullLogger<NoOpNotificacionService>.Instance,
            opciones,
            correoFake);

        await sut.NotificarSlaRevisionAsync(Notificacion, CancellationToken.None);

        correoFake.Enviados.Should().HaveCount(1);
        var enviado = correoFake.Enviados[0];
        enviado.Modulo.Should().Be("cuentas-por-pagar");
        enviado.Tipo.Should().Be("aviso-revision");
        enviado.ReferenciaId.Should().Be(FacturaId);
        enviado.Para.Should().BeEquivalentTo(["contador@millet.com.mx", "compras@millet.com.mx"]);
        enviado.Asunto.Should().Contain("plazo vencido");
        enviado.Html.Should().Contain("FAC-1234");
    }

    [Fact]
    public async Task Cuando_transporte_falla_con_error_lanza_excepcion_para_reintento()
    {
        var opciones = Options.Create(new AvisosRevisionOptions
        {
            Proveedor = "Graph",
            ContadorGeneral = ["contador@millet.com.mx"]
        });
        var correoFake = new FakeCorreoSaliente(esSimulado: false, fallarConError: true);
        var sut = new NoOpNotificacionService(
            NullLogger<NoOpNotificacionService>.Instance,
            opciones,
            correoFake);

        var act = () => sut.NotificarSlaRevisionAsync(Notificacion, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*FALLO_GRAPH*");
    }

    [Fact]
    public async Task Cuando_Proveedor_es_NoOp_no_despacha_por_transporte()
    {
        var opciones = Options.Create(new AvisosRevisionOptions
        {
            Proveedor = "NoOp",
            ContadorGeneral = ["contador@millet.com.mx"]
        });
        var correoFake = new FakeCorreoSaliente(esSimulado: false);
        var sut = new NoOpNotificacionService(
            NullLogger<NoOpNotificacionService>.Instance,
            opciones,
            correoFake);

        await sut.NotificarSlaRevisionAsync(Notificacion, CancellationToken.None);

        correoFake.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Cuando_transporte_es_simulado_no_emite_correo_real()
    {
        var opciones = Options.Create(new AvisosRevisionOptions
        {
            Proveedor = "Graph",
            ContadorGeneral = ["contador@millet.com.mx"]
        });
        var correoSimulado = new FakeCorreoSaliente(esSimulado: true);
        var sut = new NoOpNotificacionService(
            NullLogger<NoOpNotificacionService>.Instance,
            opciones,
            correoSimulado);

        await sut.NotificarSlaRevisionAsync(Notificacion, CancellationToken.None);

        correoSimulado.Enviados.Should().BeEmpty();
    }

    [Fact]
    public void DI_TryAddSingleton_respeta_implementacion_previa()
    {
        var services = new ServiceCollection();
        var custom = new CustomNotificacionService();
        services.AddSingleton<INotificacionService>(custom);

        var config = new ConfigurationBuilder().Build();
        services.AddCuentasPorPagarModule(config);

        using var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<INotificacionService>();

        resolved.Should().BeSameAs(custom);
    }

    private sealed class FakeCorreoSaliente(bool esSimulado, bool fallarConError = false) : ICorreoSaliente
    {
        public List<MensajeCorreo> Enviados { get; } = [];
        public bool EsSimulado => esSimulado;

        public Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
        {
            if (fallarConError)
                return Task.FromResult(ResultadoCorreoSaliente.Error("FALLO_GRAPH", "Error simulado de Graph"));

            Enviados.Add(mensaje);
            return Task.FromResult(ResultadoCorreoSaliente.Aceptado);
        }
    }

    private sealed class CustomNotificacionService : INotificacionService
    {
        public Task NotificarSlaRevisionAsync(SlaRevisionNotificacion notificacion, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
