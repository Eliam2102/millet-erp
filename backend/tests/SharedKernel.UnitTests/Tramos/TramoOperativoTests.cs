using FluentAssertions;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Tramos;
using Xunit;

namespace Millet.SharedKernel.UnitTests.Tramos;

/// <summary>M-303: detener y reanudar un tramo; reanudar lo autoriza otra persona.</summary>
public sealed class TramoOperativoTests
{
    private static readonly Guid Detiene = Guid.NewGuid(), Reanuda = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Detenido_conserva_el_ultimo_evento_correcto_y_solo_otra_persona_lo_reanuda()
    {
        var t = new TramoOperativo("aw.ingesta-pedidos");
        t.RegistrarEventoCorrecto("12 solicitudes procesadas; última 10425068 v3", Ahora.AddMinutes(-5));
        t.Detener("A+W manda pedidos duplicados", Detiene, Ahora);
        t.Detenido.Should().BeTrue();
        t.UltimoEventoCorrecto.Should().Contain("10425068");

        var propia = () => t.Reanudar("Ya quedó", Detiene, Ahora);
        propia.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("TRAMO_SEPARACION_FUNCIONES");
        t.Reanudar("TI corrigió la vista", Reanuda, Ahora.AddHours(1));
        (t.Detenido, t.ReanudadoPorId).Should().Be((false, (Guid?)Reanuda));
    }

    [Fact]
    public void No_se_detiene_dos_veces_ni_sin_motivo()
    {
        var t = new TramoOperativo("pt.captura");
        var sinMotivo = () => t.Detener("x", Detiene, Ahora);
        sinMotivo.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("TRAMO_MOTIVO_INVALIDO");
        t.Detener("Inventario físico de PT", Detiene, Ahora);
        var otraVez = () => t.Detener("Inventario físico de PT", Reanuda, Ahora);
        otraVez.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("TRAMO_YA_DETENIDO");
    }
}
