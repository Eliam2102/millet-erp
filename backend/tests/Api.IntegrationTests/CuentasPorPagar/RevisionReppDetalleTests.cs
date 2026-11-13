using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

[Collection(ApiCompartida.Nombre)]
public sealed class RevisionReppDetalleTests(WebApplicationFactory<Program> factory)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GET_contexto_REPP_aisla_empresa_documento_revertidos_y_conserva_historial(bool conHistorial)
    {
        await using var app = factory.ConEmpresa(EmpresaBootstrapId);
        using var client = await LoginAsync(app);
        var anio = Random.Shared.Next(2300, 2900);
        var fecha = new DateOnly(anio, 2, 15);
        var instante = new DateTimeOffset(anio, 2, 15, 12, 0, 0, TimeSpan.Zero);
        await AsegurarPeriodosAbiertosAsync(client, fecha);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var empresa = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        var proveedor = Guid.NewGuid();
        Factura Crear(Guid e) => Factura.CapturarSinOc(e, null, null, proveedor, Guid.NewGuid(), "FIX-FB15", null,
            instante, instante, fecha, "MXN", null, 100, 0, 0, 0, 100, "Muestra ficticia FB15", instante);
        var propia = Crear(EmpresaBootstrapId);
        var ajena = Crear(Guid.NewGuid());
        if (conHistorial) { propia.Autorizar(null, instante); propia.RegistrarPago(40, instante, "Pago ficticio FB15"); }
        propia.EnviarARevision(Factura.MotivoFaltaReppId, Guid.NewGuid(), null, instante);
        ajena.EnviarARevision(Factura.MotivoFaltaReppId, Guid.NewGuid(), null, instante);
        var valido = new PagoProveedorLocal(EmpresaBootstrapId, propia.Id, Guid.NewGuid(), 40, fecha);
        valido.AsignarMoneda("MXN", null);
        var revertido = new PagoProveedorLocal(EmpresaBootstrapId, propia.Id, Guid.NewGuid(), 10, fecha);
        revertido.Revertir();
        var otroDocumento = new PagoProveedorLocal(EmpresaBootstrapId, ajena.Id, Guid.NewGuid(), 20, fecha);
        // Proyección ficticia inconsistente: la lectura debe conservar el alcance de empresa.
        var otraEmpresa = new PagoProveedorLocal(ajena.EmpresaId, propia.Id, Guid.NewGuid(), 30, fecha);
        db.FacturasProveedor.AddRange(propia, ajena);
        if (conHistorial) db.PagosProveedorLocal.AddRange(valido, revertido, otroDocumento, otraEmpresa);
        using (empresa.Bypass()) await db.SaveChangesAsync();
        var idsPagos = new[] { valido.Id, revertido.Id, otroDocumento.Id, otraEmpresa.Id };
        var idsEventos = db.ChangeTracker.Entries<IntegrationEventOutboxEntry>().Select(e => e.Entity.Id).ToArray();
        var version = propia.Version;
        var importe = propia.ImportePagado;
        try
        {
            using var respuesta = await client.GetAsync($"/api/v1/cuentas-por-pagar/facturas/{propia.Id}");
            respuesta.EnsureSuccessStatusCode();
            var json = await Json(respuesta);
            var contexto = json.GetProperty("revisionRepp");
            Assert.True(contexto.GetProperty("pendiente").GetBoolean());
            if (conHistorial) Assert.Equal(instante, contexto.GetProperty("ultimaAutorizacionEn").GetDateTimeOffset());
            else Assert.Equal(System.Text.Json.JsonValueKind.Null, contexto.GetProperty("ultimaAutorizacionEn").ValueKind);
            var pagos = contexto.GetProperty("pagos");
            Assert.Equal(conHistorial ? 1 : 0, pagos.GetArrayLength());
            if (conHistorial) Assert.Equal(valido.PagoId, pagos[0].GetProperty("pagoId").GetGuid());
            using var prohibida = await client.GetAsync($"/api/v1/cuentas-por-pagar/facturas/{ajena.Id}");
            Assert.Equal(HttpStatusCode.NotFound, prohibida.StatusCode);
            await db.Entry(propia).ReloadAsync();
            Assert.Equal(version, propia.Version);
            Assert.Equal(importe, propia.ImportePagado);
            Assert.Equal(EstadoPasivo.EnRevision, propia.Estado);
        }
        finally
        {
            using var bypass = empresa.Bypass();
            await db.PagosProveedorLocal.Where(p => idsPagos.Contains(p.Id)).ExecuteDeleteAsync();
            await db.FacturasProveedor.Where(f => f.Id == propia.Id || f.Id == ajena.Id).ExecuteDeleteAsync();
            await db.OutboxEntries.Where(e => idsEventos.Contains(e.Id)).ExecuteDeleteAsync();
        }
    }
}
