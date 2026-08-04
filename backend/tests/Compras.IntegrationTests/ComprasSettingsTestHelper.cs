using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests;

/// <summary>
/// Helpers para tests que dependen del flag
/// <c>ComprasSettings.AutoGenerarOcAlAutorizar</c>. Los tests que asertan
/// que el OC borrador stub se generó (BifurcacionEndpointsTests,
/// CancelarEndpointsTests, OutboxIntegrationTests) deben activar el flag
/// antes de ejecutar el flujo de autorizar, porque el default es
/// <c>false</c> (decisión owner 2026-05-13).
///
/// <para>
/// Idempotente: si la fila ya existe, actualiza el flag; si no, la crea.
/// El empresa context se bypassea porque el seed corre fuera del scope
/// de un request HTTP autenticado.
/// </para>
/// </summary>
public static class ComprasSettingsTestHelper
{
    public static async Task SetAutoGenerarOcAsync(
        this WebApplicationFactory<Program> factory,
        Guid empresaId,
        bool valor)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var existing = await db.ComprasSettings
            .FirstOrDefaultAsync(s => s.EmpresaId == empresaId);

        if (existing is null)
        {
            db.ComprasSettings.Add(new ComprasSettings(
                id: Guid.CreateVersion7(),
                empresaId: empresaId,
                autoGenerarOcAlAutorizar: valor));
        }
        else
        {
            existing.EstablecerAutoGenerarOcAlAutorizar(valor);
        }

        await db.SaveChangesAsync();
    }
}
