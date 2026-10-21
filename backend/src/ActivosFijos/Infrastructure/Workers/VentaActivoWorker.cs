using System.Security.Claims;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application;
using Millet.ActivosFijos.Application;
namespace Millet.ActivosFijos.Infrastructure.Workers;
public sealed class VentaActivoWorker(ServiceBusClient client, IServiceScopeFactory scopes, ILogger<VentaActivoWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var processor = client.CreateProcessor("facturacion-events", "activos-fijos-ventas", new ServiceBusProcessorOptions { AutoCompleteMessages = false, MaxConcurrentCalls = 1 });
        processor.ProcessMessageAsync += ProcesarAsync;
        processor.ProcessErrorAsync += e => { logger.LogError(e.Exception, "Error procesando venta de activo"); return Task.CompletedTask; };
        await processor.StartProcessingAsync(stoppingToken);
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { await processor.StopProcessingAsync(CancellationToken.None); }
    }
    private async Task ProcesarAsync(ProcessMessageEventArgs e)
    {
        if (e.Message.Subject is "facturacion.factura-venta.timbrada.v1" or "facturacion.comprobante.cancelado.v1")
        {
            using var json = JsonDocument.Parse(e.Message.Body.ToString());
            Guid Campo(string nombre) => json.RootElement.EnumerateObject().First(x => x.Name.Equals(nombre, StringComparison.OrdinalIgnoreCase)).Value.GetGuid();
            var empresa = Campo("EmpresaId");
            var factura = Campo(e.Message.Subject.Contains("timbrada") ? "FacturaVentaId" : "ComprobanteId");
            using var scope = scopes.CreateScope();
            using var origen = scope.ServiceProvider.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(VentaActivoWorker));
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var anterior = accessor.HttpContext;
            try
            {
                // Contexto de empresa explícito: el consumidor nunca desactiva el filtro multiempresa.
                accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(MilletClaimTypes.CurrentEmpresaId, empresa.ToString())], "ActivosFijosWorker")) };
                await scope.ServiceProvider.GetRequiredService<VentaActivoConsumer>().ProcesarAsync(factura, e.CancellationToken);
            }
            finally { accessor.HttpContext = anterior; }
        }
        await e.CompleteMessageAsync(e.Message, e.CancellationToken);
    }
}
