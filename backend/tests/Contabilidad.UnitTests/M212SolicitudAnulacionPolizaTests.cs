using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// M-212 (86e3zr7pq): la anulación de una póliza la pide una persona y la aprueba o rechaza otra. Datos FIX.
/// </summary>
public class M212SolicitudAnulacionPolizaTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), Poliza = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 9, 18, 0, 0, TimeSpan.Zero);
    private const string Motivo = "FIX la póliza duplicó el gasto";

    private static SolicitudAnulacionPoliza Pendiente() => new(Guid.NewGuid(), Poliza, $"  {Motivo}  ", null, null, A, "Usuario A", Ahora);

    [Fact]
    public void Nace_pendiente_con_el_motivo_limpio_y_el_solicitante()
    {
        var s = Pendiente();

        s.Estado.Should().Be(EstadoSolicitudAnulacion.Pendiente);
        s.Motivo.Should().Be(Motivo);
        s.SolicitadaPorId.Should().Be(A);
        s.PolizaAnulacionId.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("corto")]
    public void El_motivo_es_obligatorio_de_10_a_500(string? motivo)
    {
        var act = () => new SolicitudAnulacionPoliza(Guid.NewGuid(), Poliza, motivo!, null, null, A, "A", Ahora);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_ANULACION_MOTIVO_INVALIDO");
    }

    [Fact]
    public void CA2_quien_la_solicito_no_puede_aprobarla_ni_rechazarla()
    {
        var s = Pendiente();

        var aprobar = () => s.Aprobar(A, "A", Guid.NewGuid(), Ahora);
        var rechazar = () => s.Rechazar(A, "A", "FIX ya no hace falta", Ahora);

        aprobar.Should().Throw<BusinessRuleException>().WithMessage(SolicitudAnulacionPoliza.MensajeMismaPersona);
        rechazar.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_ANULACION_MISMA_PERSONA");
        s.Estado.Should().Be(EstadoSolicitudAnulacion.Pendiente);
    }

    [Fact]
    public void Sin_usuario_identificado_no_se_decide()
    {
        var act = () => Pendiente().Aprobar(null, "system", Guid.NewGuid(), Ahora);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_ANULACION_MISMA_PERSONA");
    }

    [Fact]
    public void CA3_otra_persona_la_aprueba_y_queda_la_poliza_de_anulacion()
    {
        var s = Pendiente();
        var anulacion = Guid.NewGuid();

        s.Aprobar(B, "Usuario B", anulacion, Ahora);

        s.Estado.Should().Be(EstadoSolicitudAnulacion.Aprobada);
        s.DecididaPorId.Should().Be(B);
        s.DecididaPor.Should().Be("Usuario B");
        s.PolizaAnulacionId.Should().Be(anulacion);
    }

    [Fact]
    public void CA4_otra_persona_la_rechaza_con_motivo_obligatorio()
    {
        var s = Pendiente();

        var sinMotivo = () => s.Rechazar(B, "B", " ", Ahora);
        sinMotivo.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_ANULACION_MOTIVO_RECHAZO_INVALIDO");

        s.Rechazar(B, "Usuario B", "  FIX la póliza es correcta  ", Ahora);
        s.Estado.Should().Be(EstadoSolicitudAnulacion.Rechazada);
        s.MotivoRechazo.Should().Be("FIX la póliza es correcta");
        s.PolizaAnulacionId.Should().BeNull();
    }

    [Fact]
    public void Una_solicitud_decidida_no_se_vuelve_a_decidir()
    {
        var s = Pendiente();
        s.Rechazar(B, "B", "FIX la póliza es correcta", Ahora);

        var act = () => s.Aprobar(B, "B", Guid.NewGuid(), Ahora);
        act.Should().Throw<ConflictException>().Which.Code.Should().Be("CONTAB_ANULACION_YA_DECIDIDA");
    }
}
