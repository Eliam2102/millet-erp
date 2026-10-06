using System.Text.Json;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Application.Integration.Mappers;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor.Events;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Domain.NotaCargo.Events;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.Events;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.UnitTests.Integration;

/// <summary>
/// G1.6 — campos contables opcionales (al final) en los eventos de CxP:
/// cada mapper los propaga y un JSON anterior sin ellos sigue deserializando.
/// </summary>
public sealed class EventosContablesG16Tests
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

    private static readonly Guid Proveedor = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly Guid Ceco = Guid.NewGuid();
    private static readonly RetencionCfdi[] Detalle = [new("001", null, 10m)];

    private static void DebeTraerBloque(
        Guid? proveedorId, string? uuid, decimal? subtotal, decimal? iva, decimal? retenciones,
        IReadOnlyList<RetencionDetallePayload>? detalle, string? moneda, decimal? tc, Guid? sucursal)
    {
        proveedorId.Should().Be(Proveedor);
        uuid.Should().Be("UUID-1");
        subtotal.Should().Be(1000m);
        iva.Should().Be(160m);
        retenciones.Should().Be(10m);
        detalle.Should().BeEquivalentTo(new[] { new RetencionDetallePayload("001", null, 10m) });
        moneda.Should().Be("USD");
        tc.Should().Be(17.5m);
        sucursal.Should().Be(Sucursal);
    }

    [Fact]
    public async Task G16a_factura_de_1000_mas_IVA_160_con_retencion_publica_bloque_contable()
    {
        var publisher = new CapturingPublisher();
        var lineaId = Guid.NewGuid();

        await new FacturaProveedorRegistradaMapper(publisher).Handle(new FacturaProveedorRegistradaDomainEvent(
            EmpresaId: Guid.NewGuid(), FacturaProveedorId: Guid.NewGuid(), OrdenCompraId: Guid.NewGuid(),
            TotalFactura: 1150m,
            Lineas: [new LineaFacturada(Guid.NewGuid(), lineaId, 1m, 1000m, CentroCostoId: Ceco)],
            LineasAcumuladasOc: [],
            OcurridoEn: DateTimeOffset.UtcNow,
            ProveedorId: Proveedor, Uuid: "UUID-1", Subtotal: 1000m, Iva: 160m, Retenciones: 10m,
            RetencionesDetalle: Detalle, Moneda: "USD", TipoCambio: 17.5m, SucursalId: Sucursal,
            CentroCostoId: Ceco), CancellationToken.None);

        var e = publisher.Published.Should().BeOfType<FacturaProveedorRegistradaIntegrationEvent>().Subject;
        DebeTraerBloque(e.ProveedorId, e.Uuid, e.Subtotal, e.Iva, e.Retenciones, e.RetencionesDetalle, e.Moneda, e.TipoCambio, e.SucursalId);
        e.CentroCostoId.Should().Be(Ceco);
        e.Lineas[0].CentroCostoId.Should().Be(Ceco);
    }

    [Fact]
    public async Task FacturaCanceladaMapper_propaga_bloque_contable()
    {
        var publisher = new CapturingPublisher();
        await new FacturaProveedorCanceladaMapper(publisher).Handle(new FacturaProveedorCanceladaDomainEvent(
            Guid.NewGuid(), Guid.NewGuid(), null, MotivoCancelacion.ErrorCaptura, null, DateTimeOffset.UtcNow,
            Proveedor, "UUID-1", 1000m, 160m, 10m, Detalle, "USD", 17.5m, Sucursal), CancellationToken.None);

        var e = publisher.Published.Should().BeOfType<FacturaProveedorCanceladaIntegrationEvent>().Subject;
        DebeTraerBloque(e.ProveedorId, e.Uuid, e.Subtotal, e.Iva, e.Retenciones, e.RetencionesDetalle, e.Moneda, e.TipoCambio, e.SucursalId);
    }

    [Fact]
    public async Task NotaCreditoMapper_propaga_bloque_contable()
    {
        var publisher = new CapturingPublisher();
        await new NotaCreditoProveedorRegistradaMapper(publisher).Handle(new NotaCreditoProveedorRegistradaDomainEvent(
            Guid.NewGuid(), Guid.NewGuid(), Proveedor, null, 1, 1150m, DateTimeOffset.UtcNow,
            "UUID-1", 1000m, 160m, 10m, Detalle, "USD", 17.5m, Sucursal), CancellationToken.None);

        var e = publisher.Published.Should().BeOfType<NotaCreditoProveedorRegistradaIntegrationEvent>().Subject;
        DebeTraerBloque(e.ProveedorId, e.Uuid, e.Subtotal, e.Iva, e.Retenciones, e.RetencionesDetalle, e.Moneda, e.TipoCambio, e.SucursalId);
    }

    [Fact]
    public async Task NotaCargoMapper_propaga_bloque_contable()
    {
        var publisher = new CapturingPublisher();
        await new NotaCargoAutorizadaMapper(publisher).Handle(new NotaCargoAutorizadaDomainEvent(
            Guid.NewGuid(), Guid.NewGuid(), Proveedor, 1150m, null, DateTimeOffset.UtcNow,
            "UUID-1", 1000m, 160m, 10m, Detalle, "USD", 17.5m, Sucursal), CancellationToken.None);

        var e = publisher.Published.Should().BeOfType<NotaCargoAutorizadaIntegrationEvent>().Subject;
        DebeTraerBloque(e.ProveedorId, e.Uuid, e.Subtotal, e.Iva, e.Retenciones, e.RetencionesDetalle, e.Moneda, e.TipoCambio, e.SucursalId);
    }

    [Fact]
    public async Task AnticipoMapper_propaga_cuenta_bancaria_moneda_y_tc()
    {
        var publisher = new CapturingPublisher();
        var cuenta = Guid.NewGuid();
        await new AnticipoProveedorCapturadoMapper(publisher).Handle(new AnticipoProveedorCapturadoDomainEvent(
            Guid.NewGuid(), Guid.NewGuid(), Proveedor, 500m, null, DateTimeOffset.UtcNow,
            cuenta, "USD", 17.5m), CancellationToken.None);

        var e = publisher.Published.Should().BeOfType<AnticipoProveedorCapturadoIntegrationEvent>().Subject;
        e.CuentaBancariaId.Should().Be(cuenta);
        e.Moneda.Should().Be("USD");
        e.TipoCambio.Should().Be(17.5m);
    }

    [Fact]
    public void G16b_JSON_anterior_sin_campos_nuevos_deserializa_con_null()
    {
        var id = Guid.NewGuid();
        var viejo = $$"""
            {"EmpresaId":"{{id}}","OcurridoEn":"2026-01-01T00:00:00+00:00","FacturaProveedorId":"{{id}}",
             "OrdenCompraId":"{{id}}","TotalFactura":1160,
             "Lineas":[{"LineaFacturaId":"{{id}}","LineaOcId":null,"Cantidad":1,"Importe":1000}],
             "LineasAcumuladasOc":[]}
            """;

        var e = JsonSerializer.Deserialize<FacturaProveedorRegistradaIntegrationEvent>(viejo)!;

        e.TotalFactura.Should().Be(1160m);
        e.ProveedorId.Should().BeNull();
        e.Uuid.Should().BeNull();
        e.Subtotal.Should().BeNull();
        e.Retenciones.Should().BeNull();
        e.RetencionesDetalle.Should().BeNull();
        e.SucursalId.Should().BeNull();
        e.CentroCostoId.Should().BeNull();
        e.Lineas[0].CentroCostoId.Should().BeNull();
    }
}
