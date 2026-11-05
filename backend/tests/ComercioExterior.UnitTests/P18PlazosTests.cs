using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.UnitTests;
public sealed class P18PlazosTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 11, 5);
    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
    private static Expediente Crear() => new(Guid.NewGuid(), Guid.NewGuid(), "P18", TipoExpediente.Importacion, Guid.NewGuid(), "Pruebas de plazos", Guid.NewGuid(), Guid.NewGuid(), "USD", Actor, []);
    [Fact]
    public void Incidencia_sin_plazo_o_siguiente_accion_se_rechaza()
    {
        var e = Crear(); var datos = new DatosIncidencia(TipoIncidencia.DocumentoIncorrecto, Hoy, Actor, "Documento incorrecto");
        Assert.Throws<BusinessRuleException>(() => e.AbrirIncidencia(Guid.NewGuid(), datos, [], Actor, Ahora, Hoy)).Code.Should().Be("CE_INCIDENCIA_PLAZO_REQUERIDO");
        Assert.Throws<BusinessRuleException>(() => e.AbrirIncidencia(Guid.NewGuid(), datos with { FechaCompromiso = Hoy }, [], Actor, Ahora, Hoy));
        e.Incidencias.Should().BeEmpty();
    }
    [Fact]
    public void Aviso_al_responsable_al_vencer_sin_duplicados()
    {
        var e = Crear(); var responsable = Guid.NewGuid();
        e.AbrirIncidencia(Guid.NewGuid(), new(TipoIncidencia.DocumentoIncorrecto, Hoy, responsable, "Documento incorrecto", FechaCompromiso: Hoy, SiguienteAccion: "Solicitar documento corregido"), [], Actor, Ahora, Hoy);
        e.GenerarAvisosIncidencias(Hoy, Ahora).Should().Be(0);
        e.GenerarAvisosIncidencias(Hoy.AddDays(1), Ahora.AddDays(1)).Should().Be(1);
        e.GenerarAvisosIncidencias(Hoy.AddDays(2), Ahora.AddDays(2)).Should().Be(0);
        e.AvisosIncidencias.Single(a => a.EsVencimiento).DestinatarioUsuarioId.Should().Be(responsable);
    }
}
