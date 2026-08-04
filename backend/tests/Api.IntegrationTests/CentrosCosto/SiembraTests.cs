using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.CentrosCosto.Infrastructure.Persistence.Migrations;

namespace Millet.Api.IntegrationTests.CentrosCosto;

/// <summary>
/// Tests de la migración de siembra (CECO-PR5, requisito anti-#501 §2.4):
/// el árbol real completo quedó sembrado (conteos por PREFIJO de GUID
/// congelado — nunca absolutos: los demás tests del módulo siembran y
/// limpian sus propios datos), cero huérfanos, typo 20PDPR normalizado,
/// spot-checks de contenido real, y la SEGUNDA CORRIDA no-op PROBADA:
/// se re-ejecuta la MISMA constante SQL que corre la migración (fuente
/// única) y se afirma que no truena, no duplica y no toca updated_at.
/// </summary>
public class SiembraTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SiembraTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static readonly string[] Dim1Esperadas =
        ["101=CONKAL", "102=CHICHI SUAREZ", "103=CIRCUITO", "104=CANCUN", "105=PLANTA PINTURA"];

    private sealed record Conteos(int GruposDim2, int GruposDim3, int Dim1, int Dim2, int Dim3);

    private static async Task<Conteos> ContarSembradosAsync(CentrosCostoDbContext db)
    {
        return new Conteos(
            await db.GruposDim2.CountAsync(g => g.Id.ToString().StartsWith("0000000c-0001-")),
            await db.GruposDim3.CountAsync(g => g.Id.ToString().StartsWith("0000000c-0002-")),
            await db.Dim1s.CountAsync(d => d.Id.ToString().StartsWith("0000000c-0003-")),
            await db.Dim2s.CountAsync(d => d.Id.ToString().StartsWith("0000000c-0004-")),
            await db.Dim3s.CountAsync(e => e.Id.ToString().StartsWith("0000000c-0005-")));
    }

    [Fact]
    public async Task Arbol_completo_sembrado_sin_huerfanos_y_typo_normalizado()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        // ── 1. Conteos exactos por prefijos congelados (6/44/5/57/361) ──
        var c = await ContarSembradosAsync(db);
        Assert.Equal(new Conteos(6, 44, 5, 57, 361), c);

        // ── 2. Las 5 dim1 con clave de reportes directa + nombre del Excel ──
        var dim1s = await db.Dim1s.AsNoTracking()
            .Where(d => d.Id.ToString().StartsWith("0000000c-0003-"))
            .OrderBy(d => d.Clave)
            .Select(d => d.Clave + "=" + d.Nombre)
            .ToListAsync();
        Assert.Equal(Dim1Esperadas, dim1s);

        // ── 3. Cero huérfanos: dim3 sembrada → dim2 sembrada → dim1 sembrada ──
        var dim3Huerfanas = await db.Dim3s
            .Where(e => e.Id.ToString().StartsWith("0000000c-0005-"))
            .CountAsync(e => !db.Dim2s.Any(d =>
                d.Id == e.Dim2Id && d.Id.ToString().StartsWith("0000000c-0004-")));
        Assert.Equal(0, dim3Huerfanas);

        var dim2Huerfanas = await db.Dim2s
            .Where(d => d.Id.ToString().StartsWith("0000000c-0004-"))
            .CountAsync(d => !db.Dim1s.Any(u =>
                u.Id == d.Dim1Id && u.Id.ToString().StartsWith("0000000c-0003-")));
        Assert.Equal(0, dim2Huerfanas);

        // ── 4. Typo normalizado + spot-check de contenido real ──
        var pdpr = await db.Dim2s.AsNoTracking().SingleAsync(d => d.Clave == "20PDPR");
        Assert.Equal("SERVICIOS PERIFERICOS", pdpr.Nombre);

        var gantry = await db.Dim3s.AsNoTracking().SingleAsync(e => e.Clave == "MCLC101");
        var corte = await db.Dim2s.AsNoTracking().SingleAsync(d => d.Id == gantry.Dim2Id);
        Assert.Equal("20PDMC", corte.Clave); // CORTE, bajo la dim1 101/CONKAL
        var conkal = await db.Dim1s.AsNoTracking().SingleAsync(u => u.Id == corte.Dim1Id);
        Assert.Equal("101", conkal.Clave);
        Assert.Equal("CONKAL", conkal.Nombre);
        var grupoGantry = await db.GruposDim3.AsNoTracking().SingleAsync(g => g.Id == gantry.GrupoDim3Id);
        Assert.Equal("LINEA / CORTE 1", grupoGantry.Nombre);
    }

    [Fact]
    public async Task Segunda_corrida_es_noop_probada_no_prometida()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var antes = await ContarSembradosAsync(db);
        Assert.Equal(361, antes.Dim3); // precondición: la siembra ya corrió

        var updatedAtAntes = await db.Database
            .SqlQuery<DateTimeOffset>($"SELECT max(updated_at) AS \"Value\" FROM centros_costo.dim3 WHERE id::text LIKE '0000000c-0005-%'")
            .SingleAsync();

        // Re-ejecuta EXACTAMENTE el SQL de la migración (fuente única: la
        // misma constante que corre en Up). Sin excepción = el ON CONFLICT
        // sin target absorbe PK y claves; el guard final vuelve a validar
        // 6/44/5/57/361 sobre lo ya sembrado.
        await db.Database.ExecuteSqlRawAsync(SiembraCatalogoSql.Sql);

        var despues = await ContarSembradosAsync(db);
        Assert.Equal(antes, despues); // ni una fila más

        var updatedAtDespues = await db.Database
            .SqlQuery<DateTimeOffset>($"SELECT max(updated_at) AS \"Value\" FROM centros_costo.dim3 WHERE id::text LIKE '0000000c-0005-%'")
            .SingleAsync();
        Assert.Equal(updatedAtAntes, updatedAtDespues); // tampoco hubo UPDATE encubierto
    }
}
