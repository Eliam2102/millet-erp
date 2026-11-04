using Millet.Compras.Application.Cancelar;

namespace Millet.Compras.UnitTests.Application.Cancelar;

public class CancelarRequisicionValidatorTests
{
    // F-FIN-05: sin motivo, el mensaje es humano y no el nombre técnico del campo.
    [Fact]
    public void Sin_motivo_pide_seleccionar_el_motivo_en_espanol()
    {
        var r = new CancelarRequisicionValidator().Validate(new CancelarRequisicionCommand(Guid.CreateVersion7(), Guid.Empty));
        r.Errors.Should().ContainSingle(e => e.ErrorCode == "MOTIVO_REQUERIDO" && e.ErrorMessage == "Selecciona el motivo de cancelación.");
    }
}
