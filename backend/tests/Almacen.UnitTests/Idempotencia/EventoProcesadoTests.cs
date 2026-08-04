using Millet.Almacen.Domain.Idempotencia;

namespace Millet.Almacen.UnitTests.Idempotencia;

/// <summary>
/// Tests del agregado <see cref="EventoProcesado"/> (F3-PR1).
/// </summary>
public class EventoProcesadoTests
{
    [Fact]
    public void Constructor_inicializa_campos_correctamente()
    {
        var id = Guid.NewGuid();
        var antes = DateTimeOffset.UtcNow;

        var evento = new EventoProcesado(
            eventoId: id,
            eventoTipo: "cuentas_por_pagar.factura.registrada.v1",
            observaciones: "OC=abc factura=xyz");

        evento.EventoId.Should().Be(id);
        evento.EventoTipo.Should().Be("cuentas_por_pagar.factura.registrada.v1");
        evento.Observaciones.Should().Be("OC=abc factura=xyz");
        evento.ProcesadoAt.Should().BeOnOrAfter(antes);
        evento.ProcesadoAt.Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Constructor_sin_observaciones_acepta_null()
    {
        var evento = new EventoProcesado(Guid.NewGuid(), "test.event.v1");
        evento.Observaciones.Should().BeNull();
    }

    [Fact]
    public void Constructor_con_evento_id_vacio_falla()
    {
        var act = () => new EventoProcesado(Guid.Empty, "test.event.v1");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_con_evento_tipo_vacio_falla()
    {
        var act = () => new EventoProcesado(Guid.NewGuid(), "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_con_evento_tipo_whitespace_falla()
    {
        var act = () => new EventoProcesado(Guid.NewGuid(), "   ");
        act.Should().Throw<ArgumentException>();
    }
}
