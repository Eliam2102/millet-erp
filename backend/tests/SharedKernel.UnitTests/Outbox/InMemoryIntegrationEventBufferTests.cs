using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.SharedKernel.UnitTests.Outbox;

/// <summary>
/// El buffer particiona por schema del módulo dueño (P9-H7). Drenar el
/// schema de otro módulo NO puede consumir eventos ajenos — ese era el
/// corazón del bug: el SaveChanges del timbrado de la NC (integraciones_fiscal)
/// se robaba el evento de la NC anterior (facturacion).
/// </summary>
public sealed class InMemoryIntegrationEventBufferTests
{
    private static TestEvent Ev(string eventType) =>
        new(eventType, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

    [Fact]
    public void DrainForSchema_DevuelveSoloLosDeEseSchema_YRemueve()
    {
        var buffer = new InMemoryIntegrationEventBuffer();
        buffer.Enqueue(Ev("facturacion.nota-credito.timbrada.v1"));
        buffer.Enqueue(Ev("integraciones.fiscal.configuracion-actualizada.v1"));

        var fiscal = buffer.DrainForSchema("integraciones_fiscal");
        fiscal.Should().ContainSingle().Which.EventType.Should().Be("integraciones.fiscal.configuracion-actualizada.v1");

        // La factura NO fue arrastrada por el drenado fiscal.
        var facturacion = buffer.DrainForSchema("facturacion");
        facturacion.Should().ContainSingle().Which.EventType.Should().Be("facturacion.nota-credito.timbrada.v1");

        // Segundo drenado del mismo schema: vacío (ya removidos).
        buffer.DrainForSchema("facturacion").Should().BeEmpty();
        buffer.DrainForSchema("integraciones_fiscal").Should().BeEmpty();
    }

    [Fact]
    public void ReproAmortizacionDosAnticipos_AmbasNc_SobrevivenAlDrenadoFiscal()
    {
        // Reproduce el orden exacto de AmortizarAnticiposAsync con 2 anticipos:
        // por cada NC se hace SaveChanges del contexto fiscal (timbrado) y
        // DESPUÉS se encola el evento de esa NC. El SaveChanges fiscal de la
        // iteración N+1 NO debe llevarse el evento de la NC N.
        var buffer = new InMemoryIntegrationEventBuffer();

        // Iteración 1: fiscal SaveChanges (buffer vacío) → encola NC1.
        buffer.DrainForSchema("integraciones_fiscal").Should().BeEmpty();
        buffer.Enqueue(Ev("facturacion.nota-credito.timbrada.v1")); // NC1

        // Iteración 2: fiscal SaveChanges (antes robaba NC1) → encola NC2.
        buffer.DrainForSchema("integraciones_fiscal").Should().BeEmpty();
        buffer.Enqueue(Ev("facturacion.nota-credito.timbrada.v1")); // NC2

        // SaveChanges final del contexto de Facturación: se lleva AMBAS.
        var facturacion = buffer.DrainForSchema("facturacion");
        facturacion.Should().HaveCount(2);
    }

    [Fact]
    public void DrainForSchema_PreservaOrdenDeEncolado()
    {
        var buffer = new InMemoryIntegrationEventBuffer();
        buffer.Enqueue(Ev("facturacion.factura-venta.timbrada.v1"));
        buffer.Enqueue(Ev("facturacion.nota-credito.timbrada.v1"));
        buffer.Enqueue(Ev("facturacion.recibo-pago.timbrado.v1"));

        var drained = buffer.DrainForSchema("facturacion").Select(e => e.EventType).ToArray();

        drained.Should().Equal(
            "facturacion.factura-venta.timbrada.v1",
            "facturacion.nota-credito.timbrada.v1",
            "facturacion.recibo-pago.timbrado.v1");
    }

    [Fact]
    public void DrainUnrouted_SoloLosSinRuta_NoTocaLosRuteados()
    {
        var buffer = new InMemoryIntegrationEventBuffer();
        buffer.Enqueue(Ev("identidad.usuario.rol.asignado.v1")); // sin outbox propio
        buffer.Enqueue(Ev("facturacion.factura-venta.timbrada.v1"));

        var unrouted = buffer.DrainUnrouted();
        unrouted.Should().ContainSingle().Which.EventType.Should().Be("identidad.usuario.rol.asignado.v1");

        // El ruteado sigue disponible para su propio schema.
        buffer.DrainForSchema("facturacion").Should().ContainSingle();
    }

    [Fact]
    public void BufferVacio_DrenaListasVacias()
    {
        var buffer = new InMemoryIntegrationEventBuffer();

        buffer.DrainForSchema("facturacion").Should().BeEmpty();
        buffer.DrainUnrouted().Should().BeEmpty();
    }

    private sealed record TestEvent(string EventType, Guid EmpresaId, DateTimeOffset OcurridoEn)
        : IntegrationEvent(EventType, EmpresaId, OcurridoEn);
}
