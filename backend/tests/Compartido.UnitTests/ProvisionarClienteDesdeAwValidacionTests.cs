using FluentAssertions;
using Millet.DatosMaestros.Application.Clientes;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.UnitTests;

public class ProvisionarClienteDesdeAwValidacionTests
{
    [Fact]
    public async Task Rfc_de_7_caracteres_falla_y_el_mensaje_nombra_campo_y_motivo()
    {
        var cmd = new ProvisionarClienteDesdeAwCommand("900", "CLIENTE DEMO", "DEMO001", null, null, null, null, null, null);
        var pipeline = new ValidationPipelineBehavior<ProvisionarClienteDesdeAwCommand, int>([new ProvisionarClienteDesdeAwValidator()]);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => pipeline.Handle(cmd, () => Task.FromResult(1), CancellationToken.None));

        ex.Message.Should().StartWith("Errores de validación: ").And.Contain("Rfc").And.Contain("12").And.Contain("13").And.Contain("7"); // el texto del motivo depende del idioma de FluentValidation
    }
}
