using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.Catalogos;

/// <summary>
/// Tests del script de reconciliación de unidad de medida (ADR-0046 Etapa 1b).
/// Ejercitan el MISMO SQL del runbook
/// <c>docs/operacion/reconciliacion-unidades.md</c> contra Postgres real:
/// mapeo limpio, NULL+legacy para los variables, idempotencia (no pisa FK
/// manual) y que las líneas (snapshots) no se tocan.
/// </summary>
public class ReconciliacionUnidadesTests : IClassFixture<StubsWebApplicationFactory>
{
    // Seed de unidades (ADR-0046).
    private static readonly Guid PzaId = Guid.Parse("00000002-0007-0000-0000-000000000001");
    private static readonly Guid ParId = Guid.Parse("00000002-0007-0000-0000-000000000002");
    private static readonly Guid LId = Guid.Parse("00000002-0007-0000-0000-000000000005");

    // Mantener en sync con docs/operacion/reconciliacion-unidades.md.
    private const string ReconciliacionSql = """
        WITH mapa(legacy_norm, codigo) AS (
            VALUES
                ('PZA', 'PZA'), ('PIEZA', 'PZA'),
                ('L', 'L'), ('LITRO', 'L'),
                ('KG', 'KG'),
                ('METRO', 'M'),
                ('ML', 'ML'),
                ('HR', 'HR'),
                ('PAR', 'PAR')
        )
        UPDATE compartido.articulos a
        SET unidad_medida_id = u.id
        FROM mapa m
        JOIN compartido.unidades_medida u ON u.codigo = m.codigo
        WHERE a.unidad_medida_id IS NULL
          AND upper(btrim(regexp_replace(a.unidad_medida_default, '\.$', ''))) = m.legacy_norm;
        """;

    private readonly StubsWebApplicationFactory _factory;

    public ReconciliacionUnidadesTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Reconciliacion_MapeaLimpios_DejaNullLegacyLosVariables()
    {
        var prefijo = $"RUM{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var ids = await SeedArticulosAsync(prefijo,
            "PZA.", "pza", "LITRO", "CAJA", "PPZA", "PSP");

        await EjecutarReconciliacionAsync();

        // Limpios (normalización cubre el punto y minúsculas).
        Assert.Equal(PzaId, await FkDeAsync(ids["PZA."]));
        Assert.Equal(PzaId, await FkDeAsync(ids["pza"]));
        Assert.Equal(LId, await FkDeAsync(ids["LITRO"]));

        // Empaque variable + PPZA/PSP → NULL, legacy intacto (no auto-map).
        await AssertNullConLegacyAsync(ids["CAJA"], "CAJA");
        await AssertNullConLegacyAsync(ids["PPZA"], "PPZA");
        await AssertNullConLegacyAsync(ids["PSP"], "PSP");
    }

    [Fact]
    public async Task Reconciliacion_Idempotente_NoPisaFkManual()
    {
        var prefijo = $"RUM{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var ids = await SeedArticulosAsync(prefijo, "KG", "PZA.");

        // Asignación MANUAL del artículo "KG" a otra unidad (PAR) — simula una
        // corrección hecha a mano que la reconciliación NO debe pisar.
        await WithDbAsync(async db =>
        {
            var art = await db.Articulos.FirstAsync(a => a.Id == ids["KG"]);
            art.AsignarUnidadMedida(ParId, "PAR");
            await db.SaveChangesAsync();
        });

        // Dos corridas → mismo resultado.
        await EjecutarReconciliacionAsync();
        await EjecutarReconciliacionAsync();

        // El FK manual (PAR) se respeta por el WHERE ... IS NULL.
        Assert.Equal(ParId, await FkDeAsync(ids["KG"]));
        // El otro se mapeó normal a PZA (estable entre corridas).
        Assert.Equal(PzaId, await FkDeAsync(ids["PZA."]));
    }

    [Fact]
    public async Task Reconciliacion_NoTocaSnapshotsDeLineas()
    {
        // El UPDATE apunta SOLO a compartido.articulos: ningún snapshot
        // unidad_medida de compras.requisicion_lineas debe cambiar. Checksum
        // estable antes/después.
        var antes = await ChecksumLineasAsync();
        await EjecutarReconciliacionAsync();
        var despues = await ChecksumLineasAsync();

        Assert.Equal(antes, despues);
    }

    // --- Helpers ---

    private async Task<Dictionary<string, Guid>> SeedArticulosAsync(
        string prefijo, params string[] unidades)
    {
        var ids = new Dictionary<string, Guid>();
        await WithDbAsync(async db =>
        {
            var i = 0;
            foreach (var um in unidades)
            {
                var id = Guid.CreateVersion7();
                ids[um] = id;
                db.Articulos.Add(new Articulo(
                    id: id,
                    clave: $"{prefijo}-{i++:00}",
                    nombre: $"Recon {um}",
                    unidadMedidaDefault: um));
            }
            await db.SaveChangesAsync();
        });
        return ids;
    }

    private Task EjecutarReconciliacionAsync() =>
        WithDbAsync(db => db.Database.ExecuteSqlRawAsync(ReconciliacionSql));

    private async Task<Guid?> FkDeAsync(Guid articuloId)
    {
        Guid? fk = null;
        await WithDbAsync(async db =>
        {
            fk = await db.Articulos.AsNoTracking()
                .Where(a => a.Id == articuloId)
                .Select(a => a.UnidadMedidaId)
                .FirstAsync();
        });
        return fk;
    }

    private async Task AssertNullConLegacyAsync(Guid articuloId, string legacyEsperado)
    {
        await WithDbAsync(async db =>
        {
            var a = await db.Articulos.AsNoTracking().FirstAsync(x => x.Id == articuloId);
            Assert.Null(a.UnidadMedidaId);
            Assert.Equal(legacyEsperado, a.UnidadMedidaDefault);
        });
    }

    private async Task<string> ChecksumLineasAsync()
    {
        var checksum = "";
        await WithDbAsync(async db =>
        {
            var rows = await db.Database.SqlQueryRaw<string>(
                "SELECT (count(*)::text || ':' || " +
                "COALESCE(md5(string_agg(unidad_medida, ',' ORDER BY id)), '')) AS \"Value\" " +
                "FROM compras.requisicion_lineas").ToListAsync();
            checksum = rows[0];
        });
        return checksum;
    }

    private async Task WithDbAsync(Func<CompartidoDbContext, Task> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        await action(db);
    }
}
