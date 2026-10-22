using Millet.Compras.Application.Oc.Analisis;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.Compras.UnitTests.Reportes;

public sealed class CancelacionAnalisisTests
{
    [Fact] public void DEMO_Cancelacion_posterior_no_excluye_el_corte_anterior()
    {
        var fecha = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        AuditLogEntry[] auditoria = [new() { Timestamp = fecha, Cambios = "{\"diff\":{\"Estado\":{\"antes\":3,\"despues\":5}}}" }];
        Assert.Equal(fecha, CancelacionAnalisis.Fecha(auditoria));
        Assert.True(CancelacionAnalisis.Fecha(auditoria) >= new DateTimeOffset(2026, 4, 1, 6, 0, 0, TimeSpan.Zero));
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"diff\":{\"Estado\":{\"despues\":4}}}")]
    public void No_inventa_fecha_si_no_hay_cancelacion(string cambios)
        => Assert.Null(CancelacionAnalisis.Fecha([new() { Timestamp = DateTimeOffset.UtcNow, Cambios = cambios }]));
}
