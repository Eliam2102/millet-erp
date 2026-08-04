using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.Asignaciones.Alcance;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.CentrosCosto;

/// <summary>
/// Tests de las listas planas por nivel y de la búsqueda del selector
/// (modelo Dim): filtros por padre/estatus/q con Total correcto,
/// padres/grupos resueltos (ADR-0042 — todo local al esquema), y
/// BuscarDim3 con contexto completo (grupo, Dim2, Dim1 por join local),
/// default solo-activas y opt-in de inactivas.
///
/// <para>
/// Desde CECO-PR6 el selector filtra por ALCANCE, así que aquí el handler
/// se construye a mano con bypass (<c>dim3.leer-todos</c> fake): el sujeto
/// de ESTE test es el display/contexto y los defaults de estatus — el
/// filtro de alcance tiene sus propios asserts en <c>AlcanceTests</c>.
/// </para>
/// </summary>
public class ListasYBusquedaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ListasYBusquedaTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private sealed class PermisoTotalFake : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(permiso == "centros_costo.dim3.leer-todos");
    }

    private sealed class UsuarioFake : ICurrentUserContext
    {
        public Guid? UserId => Guid.NewGuid();
        public string? UserName => "test-listas";
    }

    private static BuscarDim3Handler SelectorConBypass(CentrosCostoDbContext db) =>
        new(db, new AlcanceDim3Evaluator(db, new UsuarioFake(), new PermisoTotalFake()));

    [Fact]
    public async Task Listas_planas_filtran_y_paginan_y_busqueda_resuelve_contexto()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        Guid? dim1Id = null, grupo2Id = null, grupo3Id = null;

        try
        {
            var grupo2 = await mediator.Send(new CrearGrupoDim2Command($"G2-LIS {sufijo}"));
            grupo2Id = grupo2.Id;
            var grupo3 = await mediator.Send(new CrearGrupoDim3Command($"G3-LIS {sufijo}"));
            grupo3Id = grupo3.Id;
            var dim1 = await mediator.Send(new CrearDim1Command($"6{sufijo[..3]}", $"CONKAL LIS {sufijo}"));
            dim1Id = dim1.Id;

            var dim2 = await mediator.Send(new CrearDim2Command(dim1.Id, $"LC{sufijo[..4]}", "Corte listas", grupo2.Id));
            var gantry = await mediator.Send(new CrearDim3Command(dim2.Id, $"LG{sufijo[..4]}", "Gantry listas", grupo3.Id));
            var mesa = await mediator.Send(new CrearDim3Command(dim2.Id, $"LM{sufijo[..4]}", "Mesa listas", grupo3.Id));

            // Una Dim3 inactiva para los defaults de estatus.
            await mediator.Send(new CambiarEstatusDim3Command(mesa.Id, mesa.Version, Activar: false));

            // ── 1. Lista de Dim1: filtro q por clave O nombre (todo local) ──
            var dim1s = await mediator.Send(new ListarDim1Query(null, $"6{sufijo[..3]}", 0, 10));
            var itemDim1 = Assert.Single(dim1s.Items);
            Assert.Equal($"CONKAL LIS {sufijo}", itemDim1.Nombre);
            Assert.Equal(1, dim1s.Total);

            // ── 2. Dim2: filtro por padre + grupo resuelto ──
            var dim2s = await mediator.Send(new ListarDim2Query(dim1.Id, null, null, null, 0, 10));
            var itemDim2 = Assert.Single(dim2s.Items);
            Assert.Equal($"G2-LIS {sufijo}", itemDim2.GrupoDim2Nombre);

            // ── 3. Dim3: filtro por padre + estatus; paginación con Total ──
            var dim3Todas = await mediator.Send(new ListarDim3Query(dim2.Id, null, null, null, 0, 10));
            Assert.Equal(2, dim3Todas.Total);
            Assert.All(dim3Todas.Items, i =>
            {
                Assert.Equal($"G3-LIS {sufijo}", i.GrupoDim3Nombre);
                Assert.Equal($"LC{sufijo[..4]}", i.Dim2Clave);
            });

            var soloActivas = await mediator.Send(new ListarDim3Query(dim2.Id, null, EstatusCatalogo.Activo, null, 0, 10));
            Assert.Equal(1, soloActivas.Total);
            Assert.Equal(gantry.Id, Assert.Single(soloActivas.Items).Id);

            var pagina = await mediator.Send(new ListarDim3Query(dim2.Id, null, null, null, 1, 1));
            Assert.Equal(2, pagina.Total);
            Assert.Single(pagina.Items);

            // ── 4. Búsqueda del selector: q único (clave O nombre), contexto completo por joins locales ──
            var selector = SelectorConBypass(db);
            var porNombre = await selector.Handle(new BuscarDim3Query("Gantry listas", false, 20), CancellationToken.None);
            var hallada = Assert.Single(porNombre, i => i.Id == gantry.Id);
            Assert.Equal("Corte listas", hallada.Dim2Nombre);
            Assert.Equal($"CONKAL LIS {sufijo}", hallada.Dim1Nombre); // join local — sin puertos
            Assert.Equal($"6{sufijo[..3]}", hallada.Dim1Clave);
            Assert.Equal($"G3-LIS {sufijo}", hallada.GrupoDim3Nombre);

            var porClave = await selector.Handle(new BuscarDim3Query($"LG{sufijo[..4]}", false, 20), CancellationToken.None);
            Assert.Contains(porClave, i => i.Id == gantry.Id);

            // ── 5. Default solo activas; opt-in incluye inactivas ──
            var defaultActivas = await selector.Handle(new BuscarDim3Query("Mesa listas", false, 20), CancellationToken.None);
            Assert.DoesNotContain(defaultActivas, i => i.Id == mesa.Id);

            var conInactivas = await selector.Handle(new BuscarDim3Query("Mesa listas", true, 20), CancellationToken.None);
            var mesaItem = Assert.Single(conInactivas, i => i.Id == mesa.Id);
            Assert.Equal(EstatusCatalogo.Inactivo, mesaItem.Estatus);
        }
        finally
        {
            if (dim1Id is not null)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    DELETE FROM centros_costo.dim3 WHERE dim2_id IN
                        (SELECT id FROM centros_costo.dim2 WHERE dim1_id = {dim1Id})");
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM centros_costo.dim2 WHERE dim1_id = {dim1Id}");
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM centros_costo.dim1 WHERE id = {dim1Id}");
            }
            if (grupo2Id is not null)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM centros_costo.grupos_dim2 WHERE id = {grupo2Id}");
            if (grupo3Id is not null)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM centros_costo.grupos_dim3 WHERE id = {grupo3Id}");
        }
    }
}
