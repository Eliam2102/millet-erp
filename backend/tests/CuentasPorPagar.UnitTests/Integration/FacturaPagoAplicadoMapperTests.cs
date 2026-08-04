using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Application.Integration.Mappers;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.UnitTests.Integration;

/// <summary>
/// Cierre del PLATFORM-TODO <c>&lt;TesoreriaEventListenerCompras&gt;</c>:
/// el mapper publica <c>cuentas_por_pagar.factura.pago-aplicado.v1</c>
/// con el acumulado pagado por OC calculado por el publisher (CxP es
/// dueño de <c>importe_pagado</c>; Compras solo lo proyecta).
/// </summary>
public sealed class FacturaPagoAplicadoMapperTests
{
    [Fact]
    public async Task Sin_orden_compra_no_publica()
    {
        await using var db = CreateDb();
        var publisher = new CapturingIntegrationPublisher();
        var mapper = new FacturaPagoAplicadoMapper(publisher, db);

        await mapper.Handle(new FacturaProveedorPagoAplicadoDomainEvent(
            EmpresaId: Guid.NewGuid(),
            FacturaProveedorId: Guid.NewGuid(),
            OrdenCompraId: null,
            ImportePagadoFactura: 100m,
            OcurridoEn: DateTimeOffset.UtcNow), CancellationToken.None);

        publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Con_orden_compra_publica_acumulado_de_la_factura_corriente()
    {
        await using var db = CreateDb();
        var publisher = new CapturingIntegrationPublisher();
        var mapper = new FacturaPagoAplicadoMapper(publisher, db);

        var ocId = Guid.NewGuid();
        var facturaId = Guid.NewGuid();
        var ocurrido = DateTimeOffset.UtcNow;

        // Sin otras facturas de la OC en BD: el acumulado es el importe
        // pagado de la factura corriente (que viaja en el domain event
        // porque el SaveChanges ocurre después del mapper).
        await mapper.Handle(new FacturaProveedorPagoAplicadoDomainEvent(
            EmpresaId: Guid.NewGuid(),
            FacturaProveedorId: facturaId,
            OrdenCompraId: ocId,
            ImportePagadoFactura: 1350m,
            OcurridoEn: ocurrido), CancellationToken.None);

        var evt = publisher.Published.Should().ContainSingle()
            .Which.Should().BeOfType<FacturaPagoAplicadoIntegrationEvent>().Subject;
        evt.EventType.Should().Be("cuentas_por_pagar.factura.pago-aplicado.v1");
        evt.OrdenCompraId.Should().Be(ocId);
        evt.FacturaProveedorId.Should().Be(facturaId);
        evt.MontoPagadoAcumuladoOc.Should().Be(1350m);
        evt.ImportePagadoFactura.Should().Be(1350m);
        evt.OcurridoEn.Should().Be(ocurrido);
    }

    private static CuentasPorPagarDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxp_pago_mapper_{Guid.NewGuid():N}")
            .Options;
        return new CuentasPorPagarDbContext(options, new FakeEmpresaContext());
    }

    private sealed class CapturingIntegrationPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
        {
            Published.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
