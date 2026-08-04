using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.Asignaciones.Alcance;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.CentrosCosto;

/// <summary>
/// Tests del toolkit de consumo de la Fase E (PR1): el selector ABIERTO
/// (<see cref="BuscarDim3AbiertoQuery"/>) que NO filtra por alcance —el
/// contrapunto del filtrado que sí—, y el read-port público
/// <see cref="IDim3ReadPort"/> para el display heredado (batch, sin alcance,
/// incluye inactivas). Ver ADR-0050.
/// </summary>
public class SelectorAbiertoYReadPortTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SelectorAbiertoYReadPortTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // Usuario SIN alcance: ni leer-todos ni asignaciones → el selector
    // filtrado le daría vacío. Sirve para probar que el ABIERTO lo ignora.
    private sealed class SinAlcancePermisoFake : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }

    private sealed class UsuarioFake : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? UserName => "test-abierto";
    }

    private static BuscarDim3Handler FiltradoSinAlcance(CentrosCostoDbContext db) =>
        new(db, new AlcanceDim3Evaluator(db, new UsuarioFake(), new SinAlcancePermisoFake()));

    [Fact]
    public async Task Selector_abierto_no_filtra_por_alcance_y_respeta_estatus()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        Guid? dim1Id = null, grupo2Id = null, grupo3Id = null;

        try
        {
            var grupo2 = await mediator.Send(new CrearGrupoDim2Command($"G2-AB {sufijo}"));
            grupo2Id = grupo2.Id;
            var grupo3 = await mediator.Send(new CrearGrupoDim3Command($"G3-AB {sufijo}"));
            grupo3Id = grupo3.Id;
            var dim1 = await mediator.Send(new CrearDim1Command($"7{sufijo[..3]}", $"CONKAL AB {sufijo}"));
            dim1Id = dim1.Id;
            var dim2 = await mediator.Send(new CrearDim2Command(dim1.Id, $"AC{sufijo[..4]}", "Corte abierto", grupo2.Id));

            var gantry = await mediator.Send(new CrearDim3Command(dim2.Id, $"AG{sufijo[..4]}", $"Gantry {sufijo}", grupo3.Id));
            var mesa = await mediator.Send(new CrearDim3Command(dim2.Id, $"AM{sufijo[..4]}", $"Mesa {sufijo}", grupo3.Id));
            await mediator.Send(new CambiarEstatusDim3Command(mesa.Id, mesa.Version, Activar: false));

            // ── Abierto: default solo activas → la Gantry, no la Mesa inactiva ──
            var activas = await mediator.Send(new BuscarDim3AbiertoQuery(sufijo, false, 20));
            Assert.Contains(activas, i => i.Id == gantry.Id);
            Assert.DoesNotContain(activas, i => i.Id == mesa.Id);
            // Contexto completo por join local (sin puertos).
            var item = Assert.Single(activas, i => i.Id == gantry.Id);
            Assert.Equal("Corte abierto", item.Dim2Nombre);
            Assert.Equal($"CONKAL AB {sufijo}", item.Dim1Nombre);

            // ── Abierto opt-in inactivas → ambas ──
            var conInactivas = await mediator.Send(new BuscarDim3AbiertoQuery(sufijo, true, 20));
            Assert.Contains(conInactivas, i => i.Id == gantry.Id);
            Assert.Contains(conInactivas, i => i.Id == mesa.Id);

            // ── Contraste: el FILTRADO con un usuario sin alcance devuelve VACÍO
            //    sobre los mismos datos → el abierto sí ignora el alcance ──
            var filtrado = FiltradoSinAlcance(db);
            var filtradas = await filtrado.Handle(new BuscarDim3Query(sufijo, false, 20), CancellationToken.None);
            Assert.DoesNotContain(filtradas, i => i.Id == gantry.Id);
        }
        finally
        {
            await LimpiarAsync(db, dim1Id, grupo2Id, grupo3Id);
        }
    }

    [Fact]
    public async Task ReadPort_resuelve_batch_incluyendo_inactivas_y_omite_desconocidos()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();
        var port = scope.ServiceProvider.GetRequiredService<IDim3ReadPort>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        Guid? dim1Id = null, grupo2Id = null, grupo3Id = null;

        try
        {
            var grupo2 = await mediator.Send(new CrearGrupoDim2Command($"G2-RP {sufijo}"));
            grupo2Id = grupo2.Id;
            var grupo3 = await mediator.Send(new CrearGrupoDim3Command($"G3-RP {sufijo}"));
            grupo3Id = grupo3.Id;
            var dim1 = await mediator.Send(new CrearDim1Command($"8{sufijo[..3]}", $"CONKAL RP {sufijo}"));
            dim1Id = dim1.Id;
            var dim2 = await mediator.Send(new CrearDim2Command(dim1.Id, $"RC{sufijo[..4]}", "Corte readport", grupo2.Id));

            var gantry = await mediator.Send(new CrearDim3Command(dim2.Id, $"RG{sufijo[..4]}", "Gantry readport", grupo3.Id));
            var mesa = await mediator.Send(new CrearDim3Command(dim2.Id, $"RM{sufijo[..4]}", "Mesa readport", grupo3.Id));
            await mediator.Send(new CambiarEstatusDim3Command(mesa.Id, mesa.Version, Activar: false));

            var desconocido = Guid.NewGuid();
            var dict = await port.ObtenerAsync(
                new[] { gantry.Id, mesa.Id, desconocido }, CancellationToken.None);

            // Activa resuelta con su Activa=true.
            Assert.True(dict.ContainsKey(gantry.Id));
            Assert.Equal("RG" + sufijo[..4], dict[gantry.Id].Clave);
            Assert.True(dict[gantry.Id].Activa);

            // Inactiva TAMBIÉN se resuelve (ADR-0049), con Activa=false.
            Assert.True(dict.ContainsKey(mesa.Id));
            Assert.False(dict[mesa.Id].Activa);

            // Id desconocido: ausente del diccionario (el consumidor hace fallback).
            Assert.False(dict.ContainsKey(desconocido));
        }
        finally
        {
            await LimpiarAsync(db, dim1Id, grupo2Id, grupo3Id);
        }
    }

    private static async Task LimpiarAsync(
        CentrosCostoDbContext db, Guid? dim1Id, Guid? grupo2Id, Guid? grupo3Id)
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
