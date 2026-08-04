using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.IntegrationTests.CentrosCosto;

/// <summary>
/// Tests de la baja lógica en CASCADA del catálogo (modelo Dim, ADR-0049):
/// desactivar un padre desactiva su subárbol vivo en un solo SaveChanges;
/// reactivar NO reactiva hijos; el guardrail CECO_PADRE_INACTIVO bloquea
/// crear/reactivar bajo un padre muerto; la baja es idempotente.
///
/// <para>Fixture 100%% local al esquema (el árbol se construye por
/// commands) con teardown en finally.</para>
/// </summary>
public class BajaCascadaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BajaCascadaTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Cascada_dim1_y_dim2_reactivacion_sin_cascada_y_guardrail_padre_inactivo()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        Guid? dim1Id = null, grupo2Id = null, grupo3Id = null;

        try
        {
            // Fixture: 1 Dim1, 2 Dim2 (A con 2 Dim3, B con 1).
            var grupo2 = await mediator.Send(new CrearGrupoDim2Command($"G2-CASC {sufijo}"));
            grupo2Id = grupo2.Id;
            var grupo3 = await mediator.Send(new CrearGrupoDim3Command($"G3-CASC {sufijo}"));
            grupo3Id = grupo3.Id;
            var dim1 = await mediator.Send(new CrearDim1Command($"8{sufijo[..3]}", $"PLANTA CASC {sufijo}"));
            dim1Id = dim1.Id;

            var dim2A = await mediator.Send(new CrearDim2Command(dim1.Id, $"A{sufijo[..5]}", "Dim2 A", grupo2.Id));
            var dim2B = await mediator.Send(new CrearDim2Command(dim1.Id, $"B{sufijo[..5]}", "Dim2 B", grupo2.Id));
            var dim3A1 = await mediator.Send(new CrearDim3Command(dim2A.Id, $"A1{sufijo[..4]}", "Dim3 A1", grupo3.Id));
            var dim3A2 = await mediator.Send(new CrearDim3Command(dim2A.Id, $"A2{sufijo[..4]}", "Dim3 A2", grupo3.Id));
            var dim3B1 = await mediator.Send(new CrearDim3Command(dim2B.Id, $"B1{sufijo[..4]}", "Dim3 B1", grupo3.Id));

            // ── 1. Cascada de Dim2: A cae con SUS Dim3; B intacta ──
            var bajaA = await mediator.Send(new DesactivarDim2Command(dim2A.Id, dim2A.Version));
            Assert.Equal(EstatusCatalogo.Inactivo, bajaA.Estatus);
            Assert.Equal(2, bajaA.Dim3Desactivadas);

            Assert.Equal(EstatusCatalogo.Inactivo,
                (await db.Dim3s.AsNoTracking().FirstAsync(e => e.Id == dim3A1.Id)).Estatus);
            Assert.Equal(EstatusCatalogo.Inactivo,
                (await db.Dim3s.AsNoTracking().FirstAsync(e => e.Id == dim3A2.Id)).Estatus);
            Assert.Equal(EstatusCatalogo.Activo,
                (await db.Dim3s.AsNoTracking().FirstAsync(e => e.Id == dim3B1.Id)).Estatus);

            // ── 2. Guardrail: crear/reactivar bajo la Dim2 A inactiva → 422 ──
            var crearBajoMuerta = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                mediator.Send(new CrearDim3Command(dim2A.Id, $"A3{sufijo[..4]}", "No debe nacer", grupo3.Id)));
            Assert.Equal("CECO_PADRE_INACTIVO", crearBajoMuerta.Code);

            var versionA1 = (await db.Dim3s.AsNoTracking().FirstAsync(e => e.Id == dim3A1.Id)).Version;
            var reactivarBajoMuerta = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                mediator.Send(new CambiarEstatusDim3Command(dim3A1.Id, versionA1, Activar: true)));
            Assert.Equal("CECO_PADRE_INACTIVO", reactivarBajoMuerta.Code);

            // ── 3. Cascada de Dim1: cae el resto vivo (Dim2 B + Dim3 B1) ──
            var versionDim1 = (await db.Dim1s.AsNoTracking().FirstAsync(d => d.Id == dim1.Id)).Version;
            var bajaDim1 = await mediator.Send(new DesactivarDim1Command(dim1.Id, versionDim1));
            Assert.Equal(1, bajaDim1.Dim2Desactivadas); // A ya estaba inactiva
            Assert.Equal(1, bajaDim1.Dim3Desactivadas); // solo B1 seguía viva

            var vivas = await db.Dim2s.AsNoTracking()
                .CountAsync(d => d.Dim1Id == dim1.Id && d.Estatus != EstatusCatalogo.Inactivo);
            Assert.Equal(0, vivas);

            // ── 4. Idempotente: segunda baja = no-op con conteos en cero ──
            versionDim1 = (await db.Dim1s.AsNoTracking().FirstAsync(d => d.Id == dim1.Id)).Version;
            var segundaBaja = await mediator.Send(new DesactivarDim1Command(dim1.Id, versionDim1));
            Assert.Equal(0, segundaBaja.Dim2Desactivadas);
            Assert.Equal(0, segundaBaja.Dim3Desactivadas);

            // ── 5. Reactivar NO cascada: la Dim1 revive, los hijos siguen muertos ──
            versionDim1 = (await db.Dim1s.AsNoTracking().FirstAsync(d => d.Id == dim1.Id)).Version;
            var reactivada = await mediator.Send(new ReactivarDim1Command(dim1.Id, versionDim1));
            Assert.Equal(EstatusCatalogo.Activo, reactivada.Estatus);

            var dim2Inactivas = await db.Dim2s.AsNoTracking()
                .CountAsync(d => d.Dim1Id == dim1.Id && d.Estatus == EstatusCatalogo.Inactivo);
            Assert.Equal(2, dim2Inactivas);

            // ── 6. Reactivación descendente con intención: Dim2 B (padre ya activo) ──
            var versionB = (await db.Dim2s.AsNoTracking().FirstAsync(d => d.Id == dim2B.Id)).Version;
            var dim2BReactivada = await mediator.Send(new ReactivarDim2Command(dim2B.Id, versionB));
            Assert.Equal(EstatusCatalogo.Activo, dim2BReactivada.Estatus);

            Assert.Equal(EstatusCatalogo.Inactivo,
                (await db.Dim3s.AsNoTracking().FirstAsync(e => e.Id == dim3B1.Id)).Estatus);
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
