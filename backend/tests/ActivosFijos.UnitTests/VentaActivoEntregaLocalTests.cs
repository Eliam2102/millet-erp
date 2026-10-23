using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Infrastructure.Workers;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.ActivosFijos.UnitTests;

/// <summary>
/// C1.8 — entrega local sin Service Bus de <c>facturacion-events</c> a
/// <see cref="VentaActivoWorker"/>: la falla del consumidor se propaga para que el
/// outbox reintente la fila; un payload inválido se descarta sin lanzar; los
/// eventos ajenos a la venta de activos se ignoran.
/// </summary>
public sealed class VentaActivoEntregaLocalTests
{
    private const string Timbrada = "facturacion.factura-venta.timbrada.v1";
    private const string Cancelado = "facturacion.comprobante.cancelado.v1";

    [Fact]
    public async Task Expone_el_topic_de_facturacion_y_relanza_con_el_contexto_de_empresa_del_evento()
    {
        var puerto = new PuertoQueFalla();
        await using var provider = CrearProveedor(puerto);
        // Implementación explícita de la interfaz: se consume como ILocalIntegrationEventConsumer.
        ILocalIntegrationEventConsumer consumidor = Worker(provider);
        var empresa = Guid.NewGuid();
        var factura = Guid.NewGuid();

        consumidor.TopicName.Should().Be("facturacion-events");
        var body = $$"""{"EmpresaId":"{{empresa}}","FacturaVentaId":"{{factura}}"}""";
        var act = () => consumidor.ConsumirLocalAsync(Timbrada, Guid.NewGuid(), body, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*lectura de la venta falló*");
        puerto.Factura.Should().Be(factura);
        puerto.Empresa.Should().Be(empresa.ToString());
    }

    [Fact]
    public async Task Cancelacion_usa_el_comprobante_como_factura()
    {
        var puerto = new PuertoQueFalla();
        await using var provider = CrearProveedor(puerto);
        var comprobante = Guid.NewGuid();

        var body = $$"""{"empresaId":"{{Guid.NewGuid()}}","comprobanteId":"{{comprobante}}"}""";
        ILocalIntegrationEventConsumer consumidor = Worker(provider);
        var act = () => consumidor.ConsumirLocalAsync(Cancelado, Guid.NewGuid(), body, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        puerto.Factura.Should().Be(comprobante);
    }

    [Fact]
    public async Task Descarta_payload_invalido_e_ignora_eventos_ajenos_sin_lanzar()
    {
        var puerto = new PuertoQueFalla();
        await using var provider = CrearProveedor(puerto);
        // Implementación explícita de la interfaz: se consume como ILocalIntegrationEventConsumer.
        ILocalIntegrationEventConsumer consumidor = Worker(provider);

        await consumidor.ConsumirLocalAsync(Timbrada, Guid.NewGuid(), "no-es-json", CancellationToken.None);
        await consumidor.ConsumirLocalAsync(Timbrada, Guid.NewGuid(), """{"EmpresaId":"x"}""", CancellationToken.None);
        await consumidor.ConsumirLocalAsync("facturacion.otro.v1", Guid.NewGuid(), "{}", CancellationToken.None);

        puerto.Factura.Should().BeNull();
    }

    private static VentaActivoWorker Worker(ServiceProvider provider)
        => new VentaActivoWorker(null!, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<VentaActivoWorker>.Instance);

    private static ServiceProvider CrearProveedor(PuertoQueFalla puerto)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<IAuditOriginContext, OrigenFalso>();
        services.AddSingleton(puerto);
        // ProcesarAsync lee la venta antes de tocar la BD: el puerto que falla basta.
        services.AddScoped(sp => new VentaActivoConsumer(null!, null!, null!, puerto, null!));
        return services.BuildServiceProvider();
    }

    private sealed class OrigenFalso : IAuditOriginContext
    {
        public string? Origin { get; private set; }
        public IDisposable SetOrigin(string origin) { Origin = origin; return new Fin(); }
        private sealed class Fin : IDisposable { public void Dispose() { } }
    }

    // Simula una falla transitoria (BD caída, Facturación no disponible, etc.) y
    // registra con qué factura y empresa se llamó.
    public sealed class PuertoQueFalla : IVentaActivoReadPort
    {
        private readonly HttpContextAccessor _accessor = new();
        public Guid? Factura { get; private set; }
        public string? Empresa { get; private set; }

        public Task<VentaActivoLectura?> ObtenerAsync(Guid factura, CancellationToken ct)
        {
            Factura = factura;
            Empresa = _accessor.HttpContext?.User.FindFirstValue(MilletClaimTypes.CurrentEmpresaId);
            throw new InvalidOperationException("La lectura de la venta falló (simulado).");
        }
    }
}
