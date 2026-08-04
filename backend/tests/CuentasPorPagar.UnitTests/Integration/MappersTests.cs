using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Application.Integration.Mappers;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.UnitTests.Integration;

/// <summary>
/// Tests de los 5 mappers de integration events (F3-PR2). Verifican
/// que cada domain event se traduzca al integration event correcto
/// con todos los campos preservados, y que el EventType v1 sea
/// estable.
/// </summary>
public sealed class MappersTests
{
    private sealed class CapturingPublisher : IIntegrationEventPublisher
    {
        public object? Published { get; private set; }
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
        {
            Published = integrationEvent;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task FacturaProveedorRegistradaMapper_traduce_con_lineas_y_acumulados()
    {
        var publisher = new CapturingPublisher();
        var mapper = new FacturaProveedorRegistradaMapper(publisher);

        var ahora = DateTimeOffset.UtcNow;
        var ocId = Guid.NewGuid();
        var facturaId = Guid.NewGuid();
        var lineaFacturaId = Guid.NewGuid();
        var lineaOcId = Guid.NewGuid();

        await mapper.Handle(new FacturaProveedorRegistradaDomainEvent(
            EmpresaId: Guid.NewGuid(),
            FacturaProveedorId: facturaId,
            OrdenCompraId: ocId,
            TotalFactura: 1160m,
            Lineas: [new LineaFacturada(lineaFacturaId, lineaOcId, 2m, 580m)],
            LineasAcumuladasOc: [new LineaOcAcumulada(lineaOcId, 5m)],
            OcurridoEn: ahora), CancellationToken.None);

        var integ = publisher.Published.Should().BeOfType<FacturaProveedorRegistradaIntegrationEvent>().Subject;
        integ.EventType.Should().Be("cuentas_por_pagar.factura.registrada.v1");
        integ.FacturaProveedorId.Should().Be(facturaId);
        integ.OrdenCompraId.Should().Be(ocId);
        integ.TotalFactura.Should().Be(1160m);
        integ.OcurridoEn.Should().Be(ahora);
        integ.Lineas.Should().ContainSingle();
        integ.Lineas[0].LineaFacturaId.Should().Be(lineaFacturaId);
        integ.Lineas[0].LineaOcId.Should().Be(lineaOcId);
        integ.LineasAcumuladasOc.Should().ContainSingle();
        integ.LineasAcumuladasOc[0].LineaOcId.Should().Be(lineaOcId);
        integ.LineasAcumuladasOc[0].CantidadAcumulada.Should().Be(5m);
    }

    [Fact]
    public async Task FacturaProveedorRechazadaPorToleranciaMapper_traduce_con_diferencia()
    {
        var publisher = new CapturingPublisher();
        var mapper = new FacturaProveedorRechazadaPorToleranciaMapper(publisher);

        await mapper.Handle(new FacturaProveedorRechazadaPorToleranciaDomainEvent(
            EmpresaId: Guid.NewGuid(),
            FacturaProveedorId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            TotalFactura: 1200m,
            TotalOc: 1160m,
            Diferencia: 40m,
            ToleranciaAplicada: "MontoAbsoluto:0.99",
            OcurridoEn: DateTimeOffset.UtcNow), CancellationToken.None);

        var integ = publisher.Published.Should().BeOfType<FacturaProveedorRechazadaPorToleranciaIntegrationEvent>().Subject;
        integ.EventType.Should().Be("cuentas_por_pagar.factura.rechazada-por-tolerancia.v1");
        integ.Diferencia.Should().Be(40m);
        integ.ToleranciaAplicada.Should().Be("MontoAbsoluto:0.99");
    }

    [Fact]
    public async Task FacturaProveedorAutorizadaMapper_usa_FechaAutorizacion_como_OcurridoEn()
    {
        var publisher = new CapturingPublisher();
        var mapper = new FacturaProveedorAutorizadaMapper(publisher);

        var fecha = DateTimeOffset.UtcNow.AddMinutes(-5);
        await mapper.Handle(new FacturaProveedorAutorizadaDomainEvent(
            EmpresaId: Guid.NewGuid(),
            FacturaProveedorId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            FechaAutorizacion: fecha), CancellationToken.None);

        var integ = publisher.Published.Should().BeOfType<FacturaProveedorAutorizadaIntegrationEvent>().Subject;
        integ.EventType.Should().Be("cuentas_por_pagar.factura.autorizada.v1");
        integ.OcurridoEn.Should().Be(fecha);
    }

    [Fact]
    public async Task FacturaProveedorCanceladaMapper_serializa_motivo_como_string()
    {
        var publisher = new CapturingPublisher();
        var mapper = new FacturaProveedorCanceladaMapper(publisher);

        await mapper.Handle(new FacturaProveedorCanceladaDomainEvent(
            EmpresaId: Guid.NewGuid(),
            FacturaProveedorId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            Motivo: MotivoCancelacion.ErrorCaptura,
            MotivoTexto: null,
            OcurridoEn: DateTimeOffset.UtcNow), CancellationToken.None);

        var integ = publisher.Published.Should().BeOfType<FacturaProveedorCanceladaIntegrationEvent>().Subject;
        integ.EventType.Should().Be("cuentas_por_pagar.factura.cancelada.v1");
        integ.Motivo.Should().Be("ErrorCaptura");
    }

    [Fact]
    public async Task DiferenciaPrecioFacturaDetectadaMapper_traduce_forma_plana_por_articulo()
    {
        // GAP-3: la forma plana espeja el payload que Almacén deserializa
        // (CxpContracts.DiferenciaPrecioFacturaDetectadaPayload) — la
        // versión anterior con PorLinea[] nunca coincidió con el consumidor.
        var publisher = new CapturingPublisher();
        var mapper = new DiferenciaPrecioFacturaDetectadaMapper(publisher);
        var articuloId = Guid.NewGuid();

        await mapper.Handle(new DiferenciaPrecioFacturaDetectadaDomainEvent(
            EmpresaId: Guid.NewGuid(),
            FacturaProveedorId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            ArticuloId: articuloId,
            CantidadFacturada: 40m,
            PrecioFacturaUnitarioMxn: 25.0125m,
            PrecioOcUnitarioMxn: 25m,
            DiferenciaUnitarioMxn: 0.0125m,
            MontoDiferenciaTotalMxn: 0.50m,
            OcurridoEn: DateTimeOffset.UtcNow), CancellationToken.None);

        var integ = publisher.Published.Should().BeOfType<DiferenciaPrecioFacturaDetectadaIntegrationEvent>().Subject;
        integ.EventType.Should().Be("cuentas_por_pagar.factura.diferencia-precio-detectada.v1");
        integ.ArticuloId.Should().Be(articuloId);
        integ.CantidadFacturada.Should().Be(40m);
        integ.PrecioOcUnitarioMxn.Should().Be(25m);
        integ.PrecioFacturaUnitarioMxn.Should().Be(25.0125m);
        integ.DiferenciaUnitarioMxn.Should().Be(0.0125m);
        integ.MontoDiferenciaTotalMxn.Should().Be(0.50m);
    }
}
