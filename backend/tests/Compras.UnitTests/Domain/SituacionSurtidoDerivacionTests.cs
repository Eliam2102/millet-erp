using Millet.Compras.Domain;

namespace Millet.Compras.UnitTests.Domain;

/// <summary>
/// Unit de la fuente única de la situación de surtido (ADR-0043). Cubre las
/// tres situaciones, la precedencia (parcial &gt; listo &gt; esperando), casos
/// mixtos a nivel de agregado, el guard de estado (solo EnSurtido) y la
/// sobre-entrega defensiva.
/// </summary>
public class SituacionSurtidoDerivacionTests
{
    [Theory]
    // (entregado, pendienteEntregar, total) → situación esperada, dentro de EnSurtido.
    [InlineData(0, 0, 10, SituacionSurtido.EsperandoCompra)]   // FF: nada entregado, nada disponible (todo en compra)
    [InlineData(0, 5, 10, SituacionSurtido.ListoParaSurtir)]   // disponible sin entregar
    [InlineData(0, 10, 10, SituacionSurtido.ListoParaSurtir)]  // todo disponible, nada entregado
    [InlineData(3, 7, 10, SituacionSurtido.SurtidoParcial)]    // algo entregado, falta
    [InlineData(3, 4, 10, SituacionSurtido.SurtidoParcial)]    // precedencia: parcial gana aunque haya disponible
    [InlineData(12, 0, 10, SituacionSurtido.SurtidoParcial)]   // sobre-entrega defensiva (entregado >= total)
    [InlineData(0, 0, 0, SituacionSurtido.EsperandoCompra)]    // degenerado (sin total) → default
    public void EnSurtido_DerivaSituacionSegunAgregados(
        int entregado, int pendiente, int total, SituacionSurtido esperada)
    {
        var actual = SituacionSurtidoDerivacion.Derivar(
            EstadoRequisicion.EnSurtido, entregado, pendiente, total);

        Assert.Equal(esperada, actual);
    }

    [Theory]
    [InlineData(EstadoRequisicion.Borrador)]
    [InlineData(EstadoRequisicion.EnAutorizacion)]
    [InlineData(EstadoRequisicion.Autorizada)]
    [InlineData(EstadoRequisicion.Cerrada)]
    [InlineData(EstadoRequisicion.Cancelada)]
    [InlineData(EstadoRequisicion.Rechazada)]
    [InlineData(EstadoRequisicion.Eliminada)]
    [InlineData(EstadoRequisicion.CerradaSinSurtir)]
    [InlineData(EstadoRequisicion.CerradaSurtidaParcial)]
    public void FueraDeEnSurtido_SituacionEsNull_AunConAgregadosNoTriviales(EstadoRequisicion estado)
    {
        // Con sumas que en EnSurtido darían SurtidoParcial; fuera de EnSurtido → null.
        var actual = SituacionSurtidoDerivacion.Derivar(estado, 5m, 5m, 10m);

        Assert.Null(actual);
    }

    [Fact]
    public void Precedencia_Mixto_ParcialGanaSobreListo()
    {
        // RQ con líneas en distinto estado: una con entrega (>0), otra con
        // disponible (>0). El agregado tiene entregado>0 y pendiente>0 →
        // SurtidoParcial por precedencia.
        var actual = SituacionSurtidoDerivacion.Derivar(
            EstadoRequisicion.EnSurtido,
            sumEntregado: 2m,
            sumPendienteEntregar: 8m,
            sumTotal: 20m);

        Assert.Equal(SituacionSurtido.SurtidoParcial, actual);
    }
}
