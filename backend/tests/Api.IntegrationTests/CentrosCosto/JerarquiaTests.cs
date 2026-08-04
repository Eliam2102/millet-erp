using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.CentrosCosto.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.CentrosCosto;

/// <summary>
/// Tests de la consulta jerárquica lazy (modelo Dim): árbol completo
/// navegable (raíz → dim1 → dim2 → dim3 hoja) con conteos de vivos, toggle
/// de inactivos, y el test de COHERENCIA árbol↔cascada: los conteos que
/// promete el nodo (Dim2Vivas/Dim3Vivas) deben coincidir EXACTAMENTE con lo
/// que la cascada reporta desactivar — mismo predicado
/// "vivo = estatus != Inactivo" (ADR-0049). Una Dim3 se pone en EnRevision
/// por SQL para blindar el predicado (cuenta como viva en ambos).
/// </summary>
public class JerarquiaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public JerarquiaTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Arbol_navegable_con_conteos_toggle_inactivos_y_coherencia_con_cascada()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        Guid? dim1Id = null, grupo2Id = null, grupo3Id = null;

        try
        {
            // Fixture: 1 Dim1, 2 Dim2 (A: 2 Dim3, B: 1), 1 Dim3 extra de A en
            // EnRevision por SQL (viva para el predicado).
            var grupo2 = await mediator.Send(new CrearGrupoDim2Command($"G2-JER {sufijo}"));
            grupo2Id = grupo2.Id;
            var grupo3 = await mediator.Send(new CrearGrupoDim3Command($"G3-JER {sufijo}"));
            grupo3Id = grupo3.Id;
            var dim1 = await mediator.Send(new CrearDim1Command($"7{sufijo[..3]}", $"PLANTA JER {sufijo}"));
            dim1Id = dim1.Id;

            var dim2A = await mediator.Send(new CrearDim2Command(dim1.Id, $"JA{sufijo[..4]}", "Corte jer", grupo2.Id));
            var dim2B = await mediator.Send(new CrearDim2Command(dim1.Id, $"JB{sufijo[..4]}", "Templado jer", grupo2.Id));
            var dim3A1 = await mediator.Send(new CrearDim3Command(dim2A.Id, $"JA1{sufijo[..3]}", "Gantry jer", grupo3.Id));
            var dim3A2 = await mediator.Send(new CrearDim3Command(dim2A.Id, $"JA2{sufijo[..3]}", "Mesa jer", grupo3.Id));
            var dim3B1 = await mediator.Send(new CrearDim3Command(dim2B.Id, $"JB1{sufijo[..3]}", "Horno jer", grupo3.Id));

            // EnRevision (estatus 2) directo en BD: viva para árbol Y cascada.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE centros_costo.dim3 SET estatus = 2 WHERE id = {dim3A2.Id}");

            // ── 1. Raíz → nodo dim1 con tipo, clave, nombre local y conteos (2 Dim2, 3 Dim3 vivas) ──
            var raiz = await mediator.Send(new ObtenerHijosJerarquiaCeCoQuery(NivelNodoCeCo.Raiz, null, false));
            var nodoDim1 = Assert.Single(raiz, n => n.Id == dim1.Id);
            Assert.Equal("dim1", nodoDim1.Tipo);
            Assert.Equal($"7{sufijo[..3]}", nodoDim1.Clave);
            Assert.Equal($"PLANTA JER {sufijo}", nodoDim1.Nombre); // columna local — sin puertos
            Assert.False(nodoDim1.EsHoja);
            Assert.Null(nodoDim1.Grupo);
            Assert.Equal(2, nodoDim1.Dim2Vivas);
            Assert.Equal(3, nodoDim1.Dim3Vivas); // la EnRevision CUENTA como viva

            // ── 2. Dim1 → nodos dim2 con grupo (chip) y conteo de Dim3 ──
            var dim2s = await mediator.Send(new ObtenerHijosJerarquiaCeCoQuery(NivelNodoCeCo.Dim1, dim1.Id, false));
            Assert.Equal(2, dim2s.Count);
            var nodoA = dim2s.Single(n => n.Id == dim2A.Id);
            Assert.Equal("dim2", nodoA.Tipo);
            Assert.Equal($"G2-JER {sufijo}", nodoA.Grupo);
            Assert.False(nodoA.EsHoja);
            Assert.Equal(2, nodoA.Dim3Vivas);

            // ── 3. Dim2 → nodos dim3 hoja con grupo ──
            var dim3s = await mediator.Send(new ObtenerHijosJerarquiaCeCoQuery(NivelNodoCeCo.Dim2, dim2A.Id, false));
            Assert.Equal(2, dim3s.Count);
            Assert.All(dim3s, n =>
            {
                Assert.Equal("dim3", n.Tipo);
                Assert.True(n.EsHoja);
                Assert.Equal($"G3-JER {sufijo}", n.Grupo);
            });

            // ── 4. Toggle de inactivos: default oculta, opt-in muestra con su estatus ──
            var versionB1 = (await db.Dim3s.AsNoTracking().FirstAsync(e => e.Id == dim3B1.Id)).Version;
            await mediator.Send(new CambiarEstatusDim3Command(dim3B1.Id, versionB1, Activar: false));

            var hijosB = await mediator.Send(new ObtenerHijosJerarquiaCeCoQuery(NivelNodoCeCo.Dim2, dim2B.Id, false));
            Assert.Empty(hijosB);
            var hijosBTodos = await mediator.Send(new ObtenerHijosJerarquiaCeCoQuery(NivelNodoCeCo.Dim2, dim2B.Id, true));
            var b1 = Assert.Single(hijosBTodos);
            Assert.Equal(EstatusCatalogo.Inactivo, b1.Estatus);

            // ── 5. COHERENCIA árbol↔cascada: lo que el nodo promete = lo que el command reporta ──
            var raizPre = await mediator.Send(new ObtenerHijosJerarquiaCeCoQuery(NivelNodoCeCo.Raiz, null, false));
            var promesa = raizPre.Single(n => n.Id == dim1.Id);
            // A esta altura: 2 Dim2 vivas; Dim3 vivas = A1 (Activa) + A2 (EnRevision); B1 ya inactiva.
            Assert.Equal(2, promesa.Dim2Vivas);
            Assert.Equal(2, promesa.Dim3Vivas);

            var versionDim1 = (await db.Dim1s.AsNoTracking().FirstAsync(d => d.Id == dim1.Id)).Version;
            var cascada = await mediator.Send(new DesactivarDim1Command(dim1.Id, versionDim1));

            Assert.Equal(promesa.Dim2Vivas, cascada.Dim2Desactivadas);
            Assert.Equal(promesa.Dim3Vivas, cascada.Dim3Desactivadas);
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
