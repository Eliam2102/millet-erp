using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Salidas;

/// <summary>
/// Tests de <see cref="SalidaRqGuard"/>: el mensaje de
/// <c>SALIDA_EXCEDE_AUTORIZADO</c> formatea la cantidad disponible con
/// formato invariante <c>0.####</c> (sin ceros de escala de la BD).
/// </summary>
public class SalidaRqGuardTests
{
    [Fact]
    public void Excede_autorizado_formatea_disponible_sin_ceros_de_escala()
    {
        var articuloId = Guid.NewGuid();
        var lineaId = Guid.NewGuid();
        var rq = new RequisicionLectura(
            Guid.NewGuid(),
            "RQ-TEST-000001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            "Autorizada",
            [new RequisicionLineaLectura(lineaId, articuloId, "PZA", 10m, 0m, null, null, 10.00000m)]);
        var lineas = new[]
        {
            new RegistrarSalidaLineaInput(articuloId, lineaId, 11m, null, null, null, null),
        };

        var act = () => SalidaRqGuard.Validar(rq, lineas);

        var ex = act.Should().Throw<BusinessRuleException>().Which;
        ex.Code.Should().Be("SALIDA_EXCEDE_AUTORIZADO");
        ex.Message.Should().Contain("(10)");
        ex.Message.Should().NotContain("10.00000");
    }
}
