using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.UnitTests;
public sealed class P18CierresTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 11, 5);
    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
    [Fact]
    public void Nueve_de_diez_y_liberacion_sin_evidencia_bloquean_cierres()
    {
        var e = Crear(); var c = Guid.NewGuid(); var linea = Guid.NewGuid();
        e.GuardarContenedor(c, new("CONT-18", null, null, "BL-18", null, null, null, null, ModalidadTransporte.Maritimo));
        e.AgregarParticipante(Guid.NewGuid(), "Agente prueba", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
        e.AgregarParticipante(Guid.NewGuid(), "Naviera prueba", TipoParticipante.Naviera, null, PapelParticipante.Captura, null, null);
        e.AsignarPartidaImportacion(c, linea, 1, 10, 10, "PZA", Actor); e.RegistrarEmbarque(c, Hoy, Hoy);
        Assert.Throws<BusinessRuleException>(() => e.CerrarDimension("fisico", Actor, Ahora, [], [], new Dictionary<Guid, decimal> { [linea] = 9 }));
        Assert.Throws<BusinessRuleException>(() => e.CerrarDimension("documental", Actor, Ahora, [], [], new Dictionary<Guid, decimal> { [linea] = 10 }));
    }
    [Fact]
    public void Tres_cierres_registrados_conservan_actor_y_fecha_y_solo_gerencia_finaliza()
    {
        var e = Crear(); e.InicializarHitos(Hoy);
        var c = Guid.NewGuid(); var linea = Guid.NewGuid(); var evidencia = Guid.NewGuid();
        e.GuardarContenedor(c, new("CONT-18", null, null, "BL-18", null, null, null, null, ModalidadTransporte.Maritimo));
        e.AgregarParticipante(Guid.NewGuid(), "Agente prueba", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
        e.AgregarParticipante(Guid.NewGuid(), "Naviera prueba", TipoParticipante.Naviera, null, PapelParticipante.Captura, null, null);
        e.AsignarPartidaImportacion(c, linea, 1, 10, 10, "PZA", Actor); e.RegistrarEmbarque(c, Hoy, Hoy);
        foreach (var h in e.Hitos.Where(h => h.Orden is > 1 and <= 11).OrderBy(h => h.Orden)) e.RegistrarHito(h.Clave, Hoy, Actor, evidencia, null, [], [evidencia], Hoy);
        var recibidas = new Dictionary<Guid, decimal> { [linea] = 10 };
        Assert.Throws<BusinessRuleException>(() => e.AprobarCierre(Actor, Ahora, [], [evidencia], recibidas));
        foreach (var dimension in new[] { "fisico", "documental", "financiero" }) e.CerrarDimension(dimension, Actor, Ahora, [], [evidencia], recibidas);
        e.Estado.Should().Be(EstadoExpediente.Abierto);
        e.Cierres([], [evidencia], recibidas).Should().OnlyContain(c => c.UsuarioId == Actor && c.Fecha == Ahora);
        var gerente = Guid.NewGuid(); e.AprobarCierre(gerente, Ahora, [], [evidencia], recibidas);
        e.Estado.Should().Be(EstadoExpediente.Cerrado); e.CierreAprobadoPorId.Should().Be(gerente);
    }
    private static Expediente Crear() => new(Guid.NewGuid(), Guid.NewGuid(), "IMP-P18", TipoExpediente.Importacion, Guid.NewGuid(), "Tres cierres", Guid.NewGuid(), Guid.NewGuid(), "USD", Actor, []);
}
