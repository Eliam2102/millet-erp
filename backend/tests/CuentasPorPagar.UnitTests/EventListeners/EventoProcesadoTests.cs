using Millet.CuentasPorPagar.Domain.Eventos;

namespace Millet.CuentasPorPagar.UnitTests.EventListeners;

public sealed class EventoProcesadoTests
{
    [Fact]
    public void Constructor_persiste_id_tipo_y_fecha()
    {
        var eventoId = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;

        var marca = new EventoProcesado(
            eventoId, "almacen.oc_recepcion.registrada.v1", ahora, "test");

        marca.EventoId.Should().Be(eventoId);
        marca.EventoTipo.Should().Be("almacen.oc_recepcion.registrada.v1");
        marca.ProcesadoEn.Should().Be(ahora);
        marca.Detalle.Should().Be("test");
    }

    [Fact]
    public void Detalle_es_opcional()
    {
        var marca = new EventoProcesado(Guid.NewGuid(), "x", DateTimeOffset.UtcNow);
        marca.Detalle.Should().BeNull();
    }
}
