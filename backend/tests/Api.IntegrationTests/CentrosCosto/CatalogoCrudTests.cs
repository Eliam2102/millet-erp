using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.IntegrationTests.CentrosCosto;

/// <summary>
/// Tests de integración del CRUD del catálogo de Centros de Costo (modelo
/// Dim, CECO-PR4): alta→editar por nivel, unicidad global →
/// ConflictException 409 y concurrencia optimista explícita
/// (VersionEsperada, ADR-0012).
///
/// <para>Con la separación total el fixture es 100%% local al esquema — el
/// árbol se construye por los propios commands, sin sembrar nada en
/// compartido. GUIDs/claves aislados + teardown en finally (molde
/// UbicacionCrudTests).</para>
/// </summary>
public class CatalogoCrudTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CatalogoCrudTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Alta_editar_unicidad_global_y_concurrencia()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        Guid? dim1Id = null, grupo2Id = null, grupo3Id = null;

        try
        {
            // ── 1. Grupos: alta + nombre duplicado → 409 ──
            var grupo2 = await mediator.Send(new CrearGrupoDim2Command($"G2 {sufijo}"));
            grupo2Id = grupo2.Id;
            var grupo3 = await mediator.Send(new CrearGrupoDim3Command($"G3 {sufijo}"));
            grupo3Id = grupo3.Id;

            var grupoDup = await Assert.ThrowsAsync<ConflictException>(() =>
                mediator.Send(new CrearGrupoDim2Command($"G2 {sufijo}")));
            Assert.Equal("CECO_GRUPO_DIM2_NOMBRE_DUPLICADO", grupoDup.Code);

            // ── 2. Dim1: alta normal (catálogo propio — sin vínculo ni puerto) + clave dup → 409 ──
            var dim1 = await mediator.Send(new CrearDim1Command($"9{sufijo[..3]}", $"PLANTA {sufijo}"));
            dim1Id = dim1.Id;
            Assert.Equal($"PLANTA {sufijo}", dim1.Nombre);

            var dim1Dup = await Assert.ThrowsAsync<ConflictException>(() =>
                mediator.Send(new CrearDim1Command($"9{sufijo[..3]}", "Otra")));
            Assert.Equal("CECO_CLAVE_DUPLICADA", dim1Dup.Code);
            Assert.Contains($"9{sufijo[..3]}", dim1Dup.Message); // la clave en conflicto viaja en el detail

            // ── 3. Dim2: alta, clave duplicada GLOBAL → 409, padre inexistente → 404 ──
            var dim2 = await mediator.Send(new CrearDim2Command(
                dim1.Id, $"D{sufijo[..5]}", "Corte test", grupo2.Id));

            var dim2Dup = await Assert.ThrowsAsync<ConflictException>(() =>
                mediator.Send(new CrearDim2Command(dim1.Id, $"D{sufijo[..5]}", "Otra", grupo2.Id)));
            Assert.Equal("CECO_CLAVE_DUPLICADA", dim2Dup.Code);

            var huerfana = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
                mediator.Send(new CrearDim2Command(Guid.NewGuid(), "DX01", "Huérfana", grupo2.Id)));
            Assert.Equal("CECO_DIM1_NO_ENCONTRADA", huerfana.Code);

            // ── 4. Dim3: alta + clave duplicada global → 409 + grupo inválido → 422 ──
            var dim3 = await mediator.Send(new CrearDim3Command(
                dim2.Id, $"E{sufijo[..5]}", "Gantry test", grupo3.Id));

            var dim3Dup = await Assert.ThrowsAsync<ConflictException>(() =>
                mediator.Send(new CrearDim3Command(dim2.Id, $"E{sufijo[..5]}", "Otra", grupo3.Id)));
            Assert.Equal("CECO_CLAVE_DUPLICADA", dim3Dup.Code);

            var grupoInvalido = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                mediator.Send(new CrearDim3Command(dim2.Id, $"EX{sufijo[..4]}", "Sin grupo", Guid.NewGuid())));
            Assert.Equal("CECO_GRUPO_DIM3_INVALIDO", grupoInvalido.Code);

            // ── 5. Editar con VersionEsperada correcta cambia clave/nombre/grupo ──
            var editada = await mediator.Send(new EditarDim3Command(
                dim3.Id, dim3.Version, $"E{sufijo[..4]}9", "Gantry test v2", grupo3.Id));
            Assert.Equal("Gantry test v2", editada.Nombre);
            Assert.True(editada.Version > dim3.Version);

            // ── 6. VersionEsperada vieja → 409 (ConcurrencyException, ADR-0012) ──
            await Assert.ThrowsAsync<ConcurrencyException>(() =>
                mediator.Send(new EditarDim3Command(
                    dim3.Id, dim3.Version, "EVIEJA", "No debe pasar", grupo3.Id)));

            // ── 7. Queries por id: contexto resuelto server-side (nunca GUID pelón) ──
            var detalle = await mediator.Send(new ObtenerDim3PorIdQuery(dim3.Id));
            Assert.Equal($"G3 {sufijo}", detalle.GrupoDim3Nombre);
            Assert.Equal("Corte test", detalle.Dim2Nombre);

            var detalleDim1 = await mediator.Send(new ObtenerDim1PorIdQuery(dim1.Id));
            Assert.Equal($"PLANTA {sufijo}", detalleDim1.Nombre);
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
