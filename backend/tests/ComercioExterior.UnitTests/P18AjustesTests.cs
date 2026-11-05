using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.UnitTests;
public sealed class P18AjustesTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 11, 5);
    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
    private static (Expediente E, Guid I) Preparar()
    {
        var e = new Expediente(Guid.NewGuid(), Guid.NewGuid(), "IMP-P18", TipoExpediente.Importacion, Guid.NewGuid(), "Ajuste tipado", Guid.NewGuid(), Guid.NewGuid(), "USD", Actor, []);
        var c = Guid.NewGuid(); e.GuardarContenedor(c, new("CONT-18", null, null, "BL-18", null, null, null, null));
        e.AsignarPartidaImportacion(c, Guid.NewGuid(), 1, 10, 10, "PZA", Actor);
        var id = Guid.NewGuid();
        e.AbrirIncidencia(id, new(TipoIncidencia.AjusteCantidades, Hoy, Actor, "Corregir cantidad embarcada", PartidaId: e.PartidasImportacion.Single().Id, FechaCompromiso: Hoy, SiguienteAccion: "Solicitar autorización", CantidadNueva: 9), [], Actor, Ahora, Hoy);
        return (e, id);
    }
    [Fact]
    public void Persona_distinta_autoriza_y_aplica_la_cantidad()
    {
        var (e, i) = Preparar();
        Assert.Throws<BusinessRuleException>(() => e.AutorizarAjuste(i, Actor, Ahora, false, 10)).Code.Should().Be("CE_AJUSTE_SEPARACION_FUNCIONES");
        e.AutorizarAjuste(i, Guid.NewGuid(), Ahora, false, 10);
        e.PartidasImportacion.Single().Cantidad.Should().Be(9);
        e.Incidencias.Single().Estado.Should().Be(EstadoIncidencia.Resuelta);
        e.Incidencias.Single().CantidadAnterior.Should().Be(10);
    }
    [Fact]
    public void Facturada_no_se_ajusta_y_no_se_evita_la_autorizacion()
    {
        var (e, i) = Preparar();
        Assert.Throws<BusinessRuleException>(() => e.AutorizarAjuste(i, Guid.NewGuid(), Ahora, true, 10)).Code.Should().Be("CE_AJUSTE_PARTIDA_FACTURADA");
        Assert.Throws<BusinessRuleException>(() => e.CambiarIncidencia(i, EstadoIncidencia.Resuelta, "Cambiar cantidad", null, [], null, Actor, Ahora, [])).Code.Should().Be("CE_AJUSTE_REQUIERE_AUTORIZACION");
        e.PartidasImportacion.Single().Cantidad.Should().Be(10);
    }
}
