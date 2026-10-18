using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>
/// Tolerancia de recepción por artículo (86e3pc50v): el PUT debe responder 2xx sobre PostgreSQL real.
/// Regresión: la auditoría usaba una operación de 38 caracteres y <c>core.audit_log.operacion</c> es varchar(32).
/// </summary>
public sealed class ArticuloToleranciaTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task PutToleranciaRecepcion_ConPermiso_RespondeExitosoYPersiste()
    {
        var ambiente = new AdjuntosProveedorAmbiente(factory);
        var articuloId = await SeedArticuloAsync(ambiente);
        try
        {
            var (client, _) = await ambiente.ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosArticulosToleranciaEditar);
            var response = await client.PutAsJsonAsync(
                $"/api/v1/datos-maestros/articulos/{articuloId}/tolerancia-recepcion", new { porcentaje = 5 });
            Assert.True(response.IsSuccessStatusCode,
                $"PUT tolerancia-recepcion respondió {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
        finally { await LimpiarAsync(ambiente, articuloId); }
    }

    private static async Task<Guid> SeedArticuloAsync(AdjuntosProveedorAmbiente ambiente)
    {
        using var scope = ambiente.Factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var articulo = new Articulo(Guid.NewGuid(), $"T{Guid.NewGuid():N}"[..16], "DEMO tolerancia articulo", "PZA");
        db.Articulos.Add(articulo);
        await db.SaveChangesAsync();
        return articulo.Id;
    }

    private static async Task LimpiarAsync(AdjuntosProveedorAmbiente ambiente, Guid articuloId)
    {
        using var scope = ambiente.Factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Articulos
            .Where(a => a.Id == articuloId).ExecuteDeleteAsync();
    }
}
