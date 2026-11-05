using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.UnitTests;
public sealed class P18DocumentosTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 11, 5);
    private static Expediente Preparar(string clave)
    {
        var e = new Expediente(Guid.NewGuid(), Guid.NewGuid(), "EXP-P18", TipoExpediente.Exportacion, Guid.NewGuid(), "Documentos por hito", null, Guid.NewGuid(), "USD", Actor, ["Cliente prueba"]);
        e.InicializarHitos(Hoy);
        e.AgregarParticipante(Guid.NewGuid(), "Agente prueba", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
        e.AutorizarSalida(Guid.NewGuid(), new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero), null, true);
        var evidencia = Guid.NewGuid();
        foreach (var h in e.Hitos.OrderBy(h => h.Orden).Where(h => h.Orden > 1 && h.Orden < e.Hitos.Single(x => x.Clave == clave).Orden))
            e.RegistrarHito(h.Clave, Hoy, Actor, evidencia, null, [], [evidencia], Hoy);
        return e;
    }
    [Fact]
    public void Documento_de_arribo_no_bloquea_embarcado()
    {
        var e = Preparar("embarcado");
        var r = new RequisitoDocumento(Guid.NewGuid(), "PRUEBA", "Documento de arribo", true, false, "arribado");
        e.RegistrarHito("embarcado", Hoy, Actor, null, null, [r], [], Hoy);
        Assert.Throws<DocumentosFaltantesException>(() => e.RegistrarHito("arribado", Hoy, Actor, null, null, [r], [], Hoy));
    }
    [Theory]
    [InlineData("despacho-aduanal")]
    [InlineData("liberado")]
    public void Evidencia_obligatoria_y_vigente(string clave)
    {
        var e = Preparar(clave);
        Assert.Throws<BusinessRuleException>(() => e.RegistrarHito(clave, Hoy, Actor, null, null, [], [], Hoy)).Code.Should().Be("CE_EVIDENCIA_HITO_REQUERIDA");
        e.Hitos.Single(h => h.Clave == clave).FechaReal.Should().BeNull();
        var a = Guid.NewGuid(); e.RegistrarHito(clave, Hoy, Actor, a, null, [], [a], Hoy);
    }
}
