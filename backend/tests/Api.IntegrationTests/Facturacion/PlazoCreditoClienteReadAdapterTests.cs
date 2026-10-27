using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.CuentasPorCobrar;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>Lectura real de CxC por cliente, moneda y estado, con líneas ficticias.</summary>
public sealed class PlazoCreditoClienteReadAdapterTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData(EstadoLineaCredito.Activa, 45)]
    [InlineData(EstadoLineaCredito.Bloqueada, 45)]
    [InlineData(EstadoLineaCredito.Suspendida, null)]
    public async Task Lee_solo_el_plazo_de_la_moneda_y_estado_admitidos(EstadoLineaCredito estado, int? esperado)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        var port = scope.ServiceProvider.GetRequiredService<IPlazoCreditoClienteReadPort>();
        Assert.IsType<PlazoCreditoClienteReadAdapter>(port);
        var empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
        var cliente = Guid.NewGuid();
        var mxn = LineaCredito.Crear(empresa, cliente, "MXN", 10000m, OrigenLineaCredito.Interno, 45);
        var usd = LineaCredito.Crear(empresa, cliente, "USD", 10000m, OrigenLineaCredito.Interno, 60);
        if (estado == EstadoLineaCredito.Bloqueada) mxn.Bloquear("Bloqueo ficticio DEMO FMT-3");
        db.AddRange(mxn, usd);
        if (estado == EstadoLineaCredito.Suspendida)
            db.Entry(mxn).Property(l => l.Estado).CurrentValue = estado;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        try
        {
            Assert.Equal(esperado, await port.ObtenerPlazoDiasAsync(cliente, "MXN", default));
            Assert.Equal(60, await port.ObtenerPlazoDiasAsync(cliente, "USD", default));
            Assert.Null(await port.ObtenerPlazoDiasAsync(Guid.NewGuid(), "MXN", default));
            Assert.Empty(db.ChangeTracker.Entries<LineaCredito>());
            db.Remove(usd);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            Assert.Null(await port.ObtenerPlazoDiasAsync(cliente, "USD", default));
        }
        finally
        {
            await db.LineasCredito.Where(l => l.ClienteId == cliente).ExecuteDeleteAsync();
        }
    }
}
