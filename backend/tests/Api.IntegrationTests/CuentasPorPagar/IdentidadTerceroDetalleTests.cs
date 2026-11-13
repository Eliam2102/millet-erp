using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Infrastructure.Outbox;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

[Collection(ApiCompartida.Nombre)]
public sealed class IdentidadTerceroDetalleTests(WebApplicationFactory<Program> factory)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A21_Detalle_resuelve_clave_sin_cambiar_identidad_y_aisla_empresa(bool conMaestro)
    {
        await using var app = factory.ConEmpresa(EmpresaBootstrapId);
        using var client = await LoginAsync(app);
        var terceroId = Guid.NewGuid();
        var clave = "A21P" + Guid.NewGuid().ToString("N")[..8];
        var rfc = "ZZZ" + Random.Shared.Next(0, 1_000_000).ToString("D6") + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant();
        var ahora = DateTimeOffset.UtcNow;
        var anio = Random.Shared.Next(2300, 2900);
        var fecha = new DateOnly(anio, 1, 15);
        var fechaDocumento = new DateTimeOffset(anio, 1, 15, 12, 0, 0, TimeSpan.Zero);
        // Periodo real aislado de prueba: conserva el validador de persistencia y el puerto productivo.
        await AsegurarPeriodosAbiertosAsync(client, fecha);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var maestros = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var proveedor = new Proveedor(terceroId, clave, "PROVEEDOR FICTICIO MISMO NOMBRE", rfc, TipoPersonaProveedor.Moral);
        if (conMaestro) { maestros.Proveedores.Add(proveedor); await maestros.SaveChangesAsync(); }
        Factura Crear(Guid empresa) => Factura.CapturarSinOc(empresa, null, null, terceroId, Guid.NewGuid(), clave, null,
            fechaDocumento, fechaDocumento, fecha, "MXN", null, 100, 0, 16, 0, 116, "Muestra ficticia A21", ahora);
        var propia = Crear(EmpresaBootstrapId);
        var ajena = Crear(Guid.NewGuid());
        db.FacturasProveedor.AddRange(propia, ajena);
        // Sólo prepara la muestra ajena; las consultas HTTP se hacen después, con su filtro real.
        using (scope.ServiceProvider.GetRequiredService<Millet.SharedKernel.Application.ICurrentEmpresaContext>().Bypass())
            await db.SaveChangesAsync();
        // Este contexto sólo agregó las dos facturas de la muestra y los eventos que produjo su guardado.
        var eventosPropios = db.ChangeTracker.Entries<IntegrationEventOutboxEntry>()
            .Select(e => e.Entity.Id).ToArray();
        try
        {
            using var respuesta = await client.GetAsync($"/api/v1/cuentas-por-pagar/facturas/{propia.Id}");
            respuesta.EnsureSuccessStatusCode();
            var json = await Json(respuesta);
            Assert.Equal(terceroId, json.GetProperty("proveedorId").GetGuid());
            Assert.Equal(conMaestro ? clave : null, json.GetProperty("proveedorClave").GetString());
            Assert.Equal(conMaestro ? proveedor.RazonSocial : null, json.GetProperty("proveedorNombre").GetString());
            using var prohibida = await client.GetAsync($"/api/v1/cuentas-por-pagar/facturas/{ajena.Id}");
            Assert.Equal(HttpStatusCode.NotFound, prohibida.StatusCode);
        }
        finally
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<Millet.SharedKernel.Application.ICurrentEmpresaContext>().Bypass();
            await db.FacturasProveedor.Where(f => f.Id == propia.Id || f.Id == ajena.Id).ExecuteDeleteAsync();
            await db.OutboxEntries.Where(e => eventosPropios.Contains(e.Id)).ExecuteDeleteAsync();
            if (conMaestro) { maestros.Proveedores.Remove(proveedor); await maestros.SaveChangesAsync(); }
        }
    }
}
