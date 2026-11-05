using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.UnitTests;
public sealed class P18SalidaTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 11, 5);
    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
    private static (Expediente E, Guid C) Crear()
    {
        var e = new Expediente(Guid.NewGuid(), Guid.NewGuid(), "EXP-P18", TipoExpediente.Exportacion, Guid.NewGuid(), "Autorización de salida", null, Guid.NewGuid(), "USD", Actor, ["Cliente prueba"]);
        var c = Guid.NewGuid(); e.GuardarContenedor(c, new("CONT-18", null, null, "BL-18", null, null, 100, 2));
        e.RegistrarPedimentoExportacion("PED-18", "A1", Hoy, Hoy, "16");
        return (e, c);
    }
    [Fact]
    public void Sin_autorizacion_no_embarca_y_no_se_autoriza_a_si_mismo()
    {
        var (e, c) = Crear();
        Assert.Throws<BusinessRuleException>(() => e.RegistrarEmbarque(c, Hoy, Hoy)).Code.Should().Be("CE_SALIDA_SIN_AUTORIZAR");
        Assert.Throws<BusinessRuleException>(() => e.AutorizarSalida(Actor, Ahora, null, true)).Code.Should().Be("CE_SALIDA_SEPARACION_FUNCIONES");
        e.AutorizarSalida(Guid.NewGuid(), Ahora, null, true);
        e.RegistrarEmbarque(c, Hoy, Hoy);
    }
    [Fact]
    public void Rechazo_exige_motivo_y_edicion_crea_version_nueva()
    {
        var (e, c) = Crear(); var gerente = Guid.NewGuid();
        Assert.Throws<BusinessRuleException>(() => e.AutorizarSalida(gerente, Ahora, "no", false));
        e.AutorizarSalida(gerente, Ahora, "Corregir el peso declarado", false);
        e.AutorizacionesSalida.Single().Motivo.Should().Be("Corregir el peso declarado");
        e.AutorizarSalida(gerente, Ahora, null, true);
        var version = e.VersionEmbarque;
        e.GuardarContenedor(c, new("CONT-18", "SELLO NUEVO", null, "BL-18", null, null, 100, 2));
        e.VersionEmbarque.Should().Be(version + 1);
        e.SalidaAutorizada.Should().BeFalse();
        Assert.Throws<BusinessRuleException>(() => e.RegistrarEmbarque(c, Hoy, Hoy));
        e.AutorizacionesSalida.Should().HaveCount(2);
        e.AutorizacionesSalida.Last().ContenidoJson.Should().NotContain("SELLO NUEVO");
    }
}
