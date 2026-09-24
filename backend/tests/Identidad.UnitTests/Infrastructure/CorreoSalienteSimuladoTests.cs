using Millet.Identidad.Application.Ports;
using Millet.Identidad.Infrastructure.Stubs;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.UnitTests.Infrastructure;

public sealed class CorreoSalienteSimuladoTests
{
    [Fact]
    public async Task No_Registra_Como_Enviado_Un_Correo_Sin_Proveedor_Real()
    {
        var correo = new CorreoAccesoColaborador(
            "contacto@ejemplo.com", "Ana", "ana@millet.mx", "temporal", "https://ejemplo.com");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new CorreoSalienteSimulado().EnviarAccesoColaboradorAsync(correo, CancellationToken.None));

        Assert.Equal("CORREO_ACCESO_NO_CONFIGURADO", error.Code);
    }
}
