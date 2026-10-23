using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Infrastructure.Workers;
using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.ActivosFijos.UnitTests;

/// <summary>
/// C1.8 — entrega local sin Service Bus: sin <c>ProcessMessageEventArgs</c> el worker
/// no puede abandonar el mensaje, así que la falla del consumidor debe propagarse
/// para que el outbox reintente; el payload inválido se descarta (como dead-letter).
/// </summary>
public sealed class ComprasActivoEntregaLocalTests
{
    private const string Recepcion = "almacen.oc_recepcion.registrada.v1";
    private const string Factura = "cuentas_por_pagar.factura.registrada.v1";

    [Fact]
    public async Task Consumidor_de_almacen_expone_su_topic_y_relanza_cuando_el_consumer_falla()
    {
        await using var provider = CrearProveedor();
        var consumidor = new ComprasActivoAlmacenLocalConsumer(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ComprasActivoWorker>.Instance);

        consumidor.TopicName.Should().Be("almacen-events");
        var body = $$"""{"empresaId":"{{Guid.NewGuid()}}","ordenCompraId":"{{Guid.NewGuid()}}","recepcionId":"{{Guid.NewGuid()}}","lineas":[]}""";
        var act = () => consumidor.ConsumirLocalAsync(Recepcion, Guid.NewGuid(), body, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*lectura de OC falló*");
    }

    [Fact]
    public async Task Consumidor_de_cxp_expone_su_topic_y_descarta_payload_invalido_sin_lanzar()
    {
        await using var provider = CrearProveedor();
        var consumidor = new ComprasActivoCxpLocalConsumer(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ComprasActivoWorker>.Instance);

        consumidor.TopicName.Should().Be("cuentas-por-pagar-events");
        await consumidor.ConsumirLocalAsync(Factura, Guid.NewGuid(), "no-es-json", CancellationToken.None);
        await consumidor.ConsumirLocalAsync("cuentas_por_pagar.otro.v1", Guid.NewGuid(), "{}", CancellationToken.None);
    }

    private static ServiceProvider CrearProveedor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext, BypassedEmpresaContext>();
        // RecibirAsync consulta la OC antes de tocar la BD: el puerto que falla basta.
        services.AddScoped(_ => new CompraActivoConsumer(null!, PuertoQueFalla.Crear()));
        return services.BuildServiceProvider();
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }

    // Simula una falla transitoria (BD caída, Compras no disponible, etc.).
    public class PuertoQueFalla : DispatchProxy
    {
        public static IComprasOcReadPort Crear() => Create<IComprasOcReadPort, PuertoQueFalla>();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new InvalidOperationException("La lectura de OC falló (simulado).");
    }
}
