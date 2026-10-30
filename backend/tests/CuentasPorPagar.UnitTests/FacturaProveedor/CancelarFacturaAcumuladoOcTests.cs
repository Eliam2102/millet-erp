using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.CuentasPorPagar.Application.FacturaProveedor.CancelarFactura;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Application.Integration.Mappers;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Tests.P3;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

/// <summary>factura.cancelada.v1 lleva el acumulado vigente por línea OC (sin la factura cancelada, incluyendo 0).</summary>
public sealed class CancelarFacturaAcumuladoOcTests
{
    private sealed class Usuario : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? UserName => "test";
    }

    private static CancelarFacturaHandler Cancelador(P3FacturaConOcTests.Ambiente a) =>
        new(a.Db, new Usuario(), new Mediator(new ServiceCollection()
            .AddSingleton<INotificationHandler<FacturaProveedorCanceladaDomainEvent>>(new FacturaProveedorCanceladaMapper(a.Eventos))
            .BuildServiceProvider()), a);

    private static async Task<FacturaProveedorCanceladaIntegrationEvent> CancelarAsync(P3FacturaConOcTests.Ambiente a, Guid id)
    {
        var f = await a.Db.FacturasProveedor.SingleAsync(x => x.Id == id);
        await Cancelador(a).Handle(new CancelarFacturaCommand(id, f.Version, MotivoCancelacion.ErrorCaptura, null), default);
        return a.Eventos.Integraciones.OfType<FacturaProveedorCanceladaIntegrationEvent>().Last();
    }

    [Fact]
    public async Task Cancelar_una_de_dos_facturas_publica_el_acumulado_restante()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();
        var f1 = await a.Handler.Handle(a.Command(4, 20), default);
        await a.Handler.Handle(a.Command(3, 20), default);

        var e = await CancelarAsync(a, f1.Id);

        e.LineasAcumuladasOc.Should().ContainSingle().Which.Should().Be(new LineaOcAcumuladaPayload(a.LineaId, 3m));
    }

    [Fact]
    public async Task Cancelar_la_unica_factura_publica_cero_para_sus_lineas()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();
        var f1 = await a.Handler.Handle(a.Command(4, 20), default);

        var e = await CancelarAsync(a, f1.Id);

        e.LineasAcumuladasOc.Should().ContainSingle().Which.Should().Be(new LineaOcAcumuladaPayload(a.LineaId, 0m));
    }

    [Fact]
    public async Task Captura_sigue_publicando_el_acumulado_de_vigentes_mas_la_corriente()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();
        await a.Handler.Handle(a.Command(4, 20), default);
        await a.Handler.Handle(a.Command(3, 20), default);

        var reg = a.Eventos.Integraciones.OfType<FacturaProveedorRegistradaIntegrationEvent>().Last();
        reg.LineasAcumuladasOc.Should().ContainSingle().Which.Should().Be(new LineaOcAcumuladaPayload(a.LineaId, 7m));
    }
}
