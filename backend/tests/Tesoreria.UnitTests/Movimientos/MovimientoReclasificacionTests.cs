using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.UnitTests.Movimientos;

/// <summary>M-204 · la reclasificación es una solicitud con segunda firma y deja rastro.</summary>
public sealed class MovimientoReclasificacionTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = DateTimeOffset.UtcNow;

    private static MovimientoReclasificacion Solicitud() =>
        MovimientoReclasificacion.Solicitar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " préstamo ", A, Ahora);

    [Fact]
    public void Solicitar_queda_pendiente_con_motivo_recortado()
    {
        var s = Solicitud();
        Assert.Equal(EstadoReclasificacion.Pendiente, s.Estado);
        Assert.Equal("préstamo", s.Motivo);
        Assert.Null(s.ResueltoPor);
    }

    [Fact]
    public void Quien_solicita_no_puede_autorizar_ni_rechazar()
    {
        var s = Solicitud();
        var e1 = Assert.Throws<BusinessRuleException>(() => s.Aprobar(A, Ahora));
        Assert.Equal("MOV_RECLASIFICACION_MISMO_USUARIO", e1.Code);
        var e2 = Assert.Throws<BusinessRuleException>(() => s.Rechazar(A, "no", Ahora));
        Assert.Equal("MOV_RECLASIFICACION_MISMO_USUARIO", e2.Code);
        Assert.Equal(EstadoReclasificacion.Pendiente, s.Estado);
    }

    [Fact]
    public void Otra_persona_aprueba_y_queda_el_rastro_y_no_se_resuelve_dos_veces()
    {
        var s = Solicitud();
        s.Aprobar(B, Ahora);
        Assert.Equal(EstadoReclasificacion.Aprobada, s.Estado);
        Assert.Equal(B, s.ResueltoPor);
        Assert.Equal(Ahora, s.ResueltoEn);
        var e = Assert.Throws<BusinessRuleException>(() => s.Aprobar(B, Ahora));
        Assert.Equal("MOV_RECLASIFICACION_YA_RESUELTA", e.Code);
    }

    [Fact]
    public void Rechazar_exige_motivo()
    {
        var s = Solicitud();
        Assert.Throws<BusinessRuleException>(() => s.Rechazar(B, " ", Ahora));
        s.Rechazar(B, "no es financiamiento", Ahora);
        Assert.Equal(EstadoReclasificacion.Rechazada, s.Estado);
        Assert.Equal("no es financiamiento", s.MotivoResolucion);
    }

    [Fact]
    public void Mismo_concepto_o_sin_motivo_se_rechaza()
    {
        var c = Guid.NewGuid();
        Assert.Throws<BusinessRuleException>(() => MovimientoReclasificacion.Solicitar(Guid.NewGuid(), c, c, "x", A, Ahora));
        Assert.Throws<BusinessRuleException>(() => MovimientoReclasificacion.Solicitar(Guid.NewGuid(), null, c, " ", A, Ahora));
    }
}
