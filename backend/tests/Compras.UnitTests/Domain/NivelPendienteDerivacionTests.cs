using Millet.Compras.Domain;

namespace Millet.Compras.UnitTests.Domain;

/// <summary>
/// Unit de la fuente única del nivel de autorización pendiente (PR-A). Cubre
/// los dos niveles dentro de EnAutorizacion (falta N1 / falta N2), el caso
/// defensivo N1+N2 (que no debería verse en EnAutorizacion) y el guard de
/// estado (fuera de EnAutorizacion ⇒ null).
/// </summary>
public class NivelPendienteDerivacionTests
{
    [Theory]
    // (tieneN1, tieneN2) → nivel pendiente esperado, dentro de EnAutorizacion.
    [InlineData(false, false, NivelAutorizacion.Nivel1)] // sin firmas → falta N1
    [InlineData(true, false, NivelAutorizacion.Nivel2)]  // N1 firmado, sin N2 → falta N2
    public void EnAutorizacion_DerivaNivelPendiente(
        bool tieneN1, bool tieneN2, NivelAutorizacion esperado)
    {
        var actual = NivelPendienteDerivacion.Derivar(
            EstadoRequisicion.EnAutorizacion, tieneN1, tieneN2);

        Assert.Equal(esperado, actual);
    }

    [Fact]
    public void EnAutorizacion_ConAmbasFirmas_EsNull_Defensivo()
    {
        // N1+N2 no debería verse en EnAutorizacion (la matriz quedaría
        // satisfecha y la RQ avanzaría); defensivamente, no hay nivel pendiente.
        var actual = NivelPendienteDerivacion.Derivar(
            EstadoRequisicion.EnAutorizacion, tieneN1: true, tieneN2: true);

        Assert.Null(actual);
    }

    [Theory]
    [InlineData(EstadoRequisicion.Borrador)]
    [InlineData(EstadoRequisicion.Autorizada)]
    [InlineData(EstadoRequisicion.EnSurtido)]
    [InlineData(EstadoRequisicion.Cerrada)]
    [InlineData(EstadoRequisicion.Cancelada)]
    [InlineData(EstadoRequisicion.Rechazada)]
    [InlineData(EstadoRequisicion.Eliminada)]
    [InlineData(EstadoRequisicion.CerradaSinSurtir)]
    [InlineData(EstadoRequisicion.CerradaSurtidaParcial)]
    public void FueraDeEnAutorizacion_EsNull_AunSinFirmas(EstadoRequisicion estado)
    {
        // Sin N1 (que en EnAutorizacion daría Nivel1); fuera de EnAutorizacion → null.
        var actual = NivelPendienteDerivacion.Derivar(estado, tieneN1: false, tieneN2: false);

        Assert.Null(actual);
    }
}
