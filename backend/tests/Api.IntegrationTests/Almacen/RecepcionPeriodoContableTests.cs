using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Infrastructure.Persistence;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>C1.2: HTTP, DI y consulta PostgreSQL reales; año FIX propio, sin modificar 2026 ni catálogos compartidos.</summary>
public sealed class RecepcionPeriodoContableTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recepcion_de_septiembre_cerrado_devuelve_422_sin_crear_movimiento(bool packingList)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        // Dentro del rango permitido por Almacén; no se usa en otras suites.
        const int anio = 2098;
        await using var calendario = new PeriodoContableFixture(db.Database.GetConnectionString()!, EmpresaBootstrapId, anio);
        await calendario.SembrarAsync(mesAbierto: 10, cerrarAnteriores: true);
        using var admin = await LoginAsync(factory);
        var ocId = Guid.NewGuid();
        var fecha = new DateOnly(anio, 9, 15);
        RegistrarRecepcionLineaInput[] lineas = [new(Guid.NewGuid(), null, 1m, null, null, Guid.NewGuid())];

        using var response = packingList
            ? await admin.PostAsJsonAsync("/api/v1/almacen/recepciones/packing-list",
                new RegistrarRecepcionConPackingListCommand(ocId, fecha, "FIX-C1.2/packing-list.pdf", null, lineas))
            : await admin.PostAsJsonAsync("/api/v1/almacen/recepciones/",
                new RegistrarRecepcionConFacturaCommand(ocId, fecha, Guid.NewGuid(), null, "FIX C1.2", lineas));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await Json(response);
        Assert.Equal("PERIODO_CONTABLE_NO_ADMITE", problem.GetProperty("code").GetString());
        Assert.Equal($"El periodo {anio}-09 está cerrado o no está abierto en Contabilidad; no se registran movimientos de almacén con esa fecha.",
            problem.GetProperty("detail").GetString());
        Assert.False(await db.Movimientos.IgnoreQueryFilters().AnyAsync(m => m.OcId == ocId));
    }
}
