using Millet.Integraciones.Fiscal.Application.IntegrationEvents;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Integraciones.Fiscal.UnitTests.Application;

/// <summary>
/// Regresión FAC-DET-PR7: <c>OutboxIntegrationEventPublisher</c> valida en
/// runtime que todo evento publicado derive de <see cref="IntegrationEvent"/>;
/// un record que lo olvide compila bien y explota con 500 al primer uso
/// (así falló guardar la ConfiguracionPac). Este test barre el assembly
/// para que el olvido truene en CI, no en producción.
/// </summary>
public sealed class IntegrationEventsContractTests
{
    [Fact]
    public void Todo_record_Event_del_modulo_deriva_de_IntegrationEvent()
    {
        var tiposEvento = typeof(IntegracionesFiscalConfiguracionActualizadaEvent)
            .Assembly.GetTypes()
            .Where(t => t.IsClass
                        && !t.IsAbstract
                        && t.Namespace?.Contains(".IntegrationEvents") == true
                        && t.Name.EndsWith("Event", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(tiposEvento);
        foreach (var tipo in tiposEvento)
        {
            Assert.True(
                typeof(IntegrationEvent).IsAssignableFrom(tipo),
                $"{tipo.FullName} no deriva de IntegrationEvent — el publisher del outbox lo rechazará en runtime (500).");
        }
    }

    [Fact]
    public void ConfiguracionActualizadaEvent_lleva_el_EventType_canonico()
    {
        var evt = new IntegracionesFiscalConfiguracionActualizadaEvent(
            ConfiguracionId: Guid.NewGuid(),
            EmpresaId: Guid.NewGuid(),
            Proveedor: 1,
            BaseUrl: "https://test.fiscalapi.com",
            Activo: true,
            Rotacion: false,
            OcurridoEn: DateTimeOffset.UtcNow);

        Assert.Equal("integraciones.fiscal.configuracion-actualizada.v1", evt.EventType);
    }
}
