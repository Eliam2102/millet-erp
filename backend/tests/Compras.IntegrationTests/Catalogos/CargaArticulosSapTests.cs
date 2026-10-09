using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.Catalogos;

/// <summary>
/// Tests de la carga de artículos de SAP (G1.12-a, G1.12-b, CA2.10).
/// Ejercitan el MISMO SQL del runbook
/// <c>docs/operacion/carga-sap-scripts/02_load_articulos_local.sql</c> contra Postgres real:
/// - Dry-run no muta el catálogo y reporta exclusiones.
/// - Inserción idempotente (ON CONFLICT DO NOTHING): una segunda corrida no duplica.
/// - InvntItem 'N' -> Naturaleza.Servicio (1); 'Y' -> Naturaleza.Estandar (0).
/// - Exclusiones automáticas por longitud (>20 clave, >254 nombre, >20 uom) y duplicados en lote.
/// </summary>
public class CargaArticulosSapTests : IClassFixture<StubsWebApplicationFactory>
{
    // Mantener en sync con docs/operacion/carga-sap-scripts/02_load_articulos_local.sql.
    private const string PrepararStagingSql = """
        CREATE SCHEMA IF NOT EXISTS staging_carga;
        DROP TABLE IF EXISTS staging_carga.articulos_sap CASCADE;
        CREATE TABLE staging_carga.articulos_sap (
            item_code      text,
            nombre         text,
            unidad_medida  text,
            invnt_item     text,
            grupo_cod      int,
            categoria      text,
            ultima_compra  date
        );

        CREATE OR REPLACE VIEW staging_carga.v_articulos_carga AS
        WITH con_dup AS (
            SELECT
                s.*,
                ROW_NUMBER() OVER (PARTITION BY TRIM(s.item_code) ORDER BY s.item_code) AS rn
            FROM staging_carga.articulos_sap s
        )
        SELECT
            TRIM(s.item_code)                              AS clave,
            TRIM(s.nombre)                                 AS nombre,
            TRIM(s.unidad_medida)                          AS unidad_medida_default,
            CASE WHEN s.invnt_item = 'N' THEN 1 ELSE 0 END AS naturaleza,
            0                                              AS estatus,
            s.categoria,
            s.grupo_cod,
            s.ultima_compra,
            CASE
                WHEN length(TRIM(s.item_code)) > 20 THEN 'clave>20'
                WHEN length(TRIM(s.nombre)) > 254 THEN 'nombre>254'
                WHEN length(TRIM(s.unidad_medida)) > 20 THEN 'uom>20'
                WHEN length(s.categoria) > 100 THEN 'categoria>100'
                WHEN s.rn > 1 THEN 'dup en lote'
                WHEN EXISTS (
                    SELECT 1 FROM compartido.producto_aw paw
                    WHERE paw.referencia_externa = TRIM(s.item_code)
                ) OR EXISTS (
                    SELECT 1 FROM compartido.producto_aw_componente pac
                    WHERE pac.componente_ref = TRIM(s.item_code)
                ) OR s.grupo_cod BETWEEN 142 AND 147 THEN 'posible A+W'  -- grupos SAP «VIDRIO *»: marcadores del vidrio que vive en A+W
                ELSE NULL
            END                                            AS motivo_exclusion
        FROM con_dup s;
        """;

    private const string InsertCargaSql = """
        CREATE TABLE IF NOT EXISTS staging_carga.articulos_insertados (
            clave varchar(20) PRIMARY KEY,
            inserted_at timestamptz NOT NULL DEFAULT now()
        );

        WITH insertados AS (
            INSERT INTO compartido.articulos
                (id, clave, nombre, unidad_medida_default, naturaleza, estatus, categoria,
                 version, created_at, updated_at)
            SELECT
                gen_random_uuid(),
                v.clave,
                v.nombre,
                v.unidad_medida_default,
                v.naturaleza,
                v.estatus,
                v.categoria,
                1,
                now(),
                now()
            FROM staging_carga.v_articulos_carga v
            WHERE v.motivo_exclusion IS NULL
            ON CONFLICT (clave) DO NOTHING
            RETURNING clave
        )
        INSERT INTO staging_carga.articulos_insertados (clave)
        SELECT clave FROM insertados;
        """;

    private readonly StubsWebApplicationFactory _factory;

    public CargaArticulosSapTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DryRun_NoModificaArticulos_YReportaExclusiones()
    {
        var prefijo = $"SAP{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var claveExistente = $"{prefijo}-EXI";
        var claveNormal = $"{prefijo}-NOR";
        var claveServicio = $"{prefijo}-SRV";
        var claveLarga = $"{prefijo}-CLAVE-MUY-LARGA-123456"; // > 20 caracteres

        // Sembrar un artículo existente en compartido.articulos
        await WithDbAsync(async db =>
        {
            db.Articulos.Add(new Articulo(
                id: Guid.CreateVersion7(),
                clave: claveExistente,
                nombre: "Articulo Existente Previo",
                unidadMedidaDefault: "PZA",
                naturaleza: Naturaleza.Estandar));
            await db.SaveChangesAsync();
        });

        var conteoAntes = await ContarArticulosTotalesAsync();

        // Preparar staging y sembrar lote de prueba
        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(PrepararStagingSql);

            // 1) Clave normal inventariable
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO staging_carga.articulos_sap VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                claveNormal, "Insumo Estandar", "PZA", "Y", 101, "Papeleria", new DateOnly(2026, 1, 1));

            // 2) Servicio ('N')
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO staging_carga.articulos_sap VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                claveServicio, "Servicio Flete", "SER", "N", 201, "Servicios", new DateOnly(2026, 2, 1));

            // 3) Duplicado en lote (misma clave que normal)
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO staging_carga.articulos_sap VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                claveNormal, "Insumo Estandar Duplicado", "PZA", "Y", 101, "Papeleria", new DateOnly(2026, 1, 1));

            // 4) Clave con longitud > 20
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO staging_carga.articulos_sap VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                claveLarga, "Articulo Con Clave Larga", "PZA", "Y", 101, "Papeleria", new DateOnly(2026, 1, 1));

            // 5) Colisión con existente
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO staging_carga.articulos_sap VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                claveExistente, "Articulo Existente en SAP", "PZA", "Y", 101, "Papeleria", new DateOnly(2026, 1, 1));
        });

        // Ejecutar consultas de dry-run (G1.12-a: solo SELECT)
        int aptos = 0, excluidos = 0, dups = 0, existentes = 0;
        await WithDbAsync(async db =>
        {
            var res = await db.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM staging_carga.v_articulos_carga WHERE motivo_exclusion IS NULL")
                .ToListAsync();
            aptos = res[0];

            var resExc = await db.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM staging_carga.v_articulos_carga WHERE motivo_exclusion IS NOT NULL")
                .ToListAsync();
            excluidos = resExc[0];

            var resDup = await db.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM staging_carga.v_articulos_carga WHERE motivo_exclusion = 'dup en lote'")
                .ToListAsync();
            dups = resDup[0];

            var resExi = await db.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM staging_carga.v_articulos_carga v " +
                "JOIN compartido.articulos a ON a.clave = v.clave WHERE v.motivo_exclusion IS NULL")
                .ToListAsync();
            existentes = resExi[0];
        });

        var conteoDespues = await ContarArticulosTotalesAsync();

        // El dry run no modificó la tabla de destino
        Assert.Equal(conteoAntes, conteoDespues);

        // Se detectaron exactamente los conteos esperados
        Assert.Equal(3, aptos); // claveNormal, claveServicio, claveExistente
        Assert.Equal(2, excluidos); // dup en lote, clave>20
        Assert.Equal(1, dups);
        Assert.Equal(1, existentes);
    }

    [Fact]
    public async Task Carga_InsertaIdempotente_MapeaServiciosYExcluyeInvalidos()
    {
        var prefijo = $"SAP{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var claveExistente = $"{prefijo}-EXI";
        var claveNormal = $"{prefijo}-NOR";
        var claveServicio = $"{prefijo}-SRV";
        var claveLarga = $"{prefijo}-CLAVE-MUY-LARGA-123456";
        var claveVidrio = $"{prefijo}-VID";

        // Sembrar existente con nombre original
        var nombreOriginalExistente = "Existente Nombre Original";
        await WithDbAsync(async db =>
        {
            db.Articulos.Add(new Articulo(
                id: Guid.CreateVersion7(),
                clave: claveExistente,
                nombre: nombreOriginalExistente,
                unidadMedidaDefault: "PZA",
                naturaleza: Naturaleza.Estandar));
            await db.SaveChangesAsync();
        });

        // Sembrar staging
        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(PrepararStagingSql);

            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO staging_carga.articulos_sap VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                claveNormal, "Insumo Estandar", "PZA", "Y", 101, "Papeleria", new DateOnly(2026, 1, 1));

            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO staging_carga.articulos_sap VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                claveServicio, "Servicio Mantenimiento", "SRV", "N", 201, "Servicios", new DateOnly(2026, 1, 1));

            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO staging_carga.articulos_sap VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                claveNormal, "Insumo Estandar Duplicado", "PZA", "Y", 101, "Papeleria", new DateOnly(2026, 1, 1));

            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO staging_carga.articulos_sap VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                claveLarga, "Invalido Clave Larga", "PZA", "Y", 101, "Papeleria", new DateOnly(2026, 1, 1));

            // Marcador de vidrio (grupo SAP 143): vive en A+W, no se carga.
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO staging_carga.articulos_sap VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                claveVidrio, "Vidrio Templado", "PZA", "N", 143, "VIDRIO TEMPLADO", new DateOnly(2026, 1, 1));

            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO staging_carga.articulos_sap VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                claveExistente, "Nombre Que No Debe Pisar", "PZA", "Y", 101, "Papeleria", new DateOnly(2026, 1, 1));
        });

        // 1ª corrida de INSERT
        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(InsertCargaSql);
        });

        // Verificaciones tras 1ª corrida
        await WithDbAsync(async db =>
        {
            // Artículo normal quedó con naturaleza Estándar (0)
            var artNormal = await db.Articulos.FirstAsync(a => a.Clave == claveNormal);
            Assert.Equal(Naturaleza.Estandar, artNormal.Naturaleza);
            Assert.Equal("Insumo Estandar", artNormal.Nombre);

            // Artículo servicio quedó con naturaleza Servicio (1)
            var artServicio = await db.Articulos.FirstAsync(a => a.Clave == claveServicio);
            Assert.Equal(Naturaleza.Servicio, artServicio.Naturaleza);
            Assert.Equal("Servicio Mantenimiento", artServicio.Nombre);

            // Artículo con clave larga NO fue insertado
            var existeLarga = await db.Articulos.AnyAsync(a => a.Clave == claveLarga);
            Assert.False(existeLarga);

            // Marcador de vidrio (posible A+W) NO fue insertado
            Assert.False(await db.Articulos.AnyAsync(a => a.Clave == claveVidrio));

            // Artículo existente NO fue sobreescrito
            var artExistente = await db.Articulos.FirstAsync(a => a.Clave == claveExistente);
            Assert.Equal(nombreOriginalExistente, artExistente.Nombre);
        });

        var conteoTrasPrimera = await ContarArticulosTotalesAsync();

        // 2ª corrida de INSERT (G1.12-b: Idempotencia)
        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(InsertCargaSql);
        });

        var conteoTrasSegunda = await ContarArticulosTotalesAsync();

        // La segunda corrida no agrega filas (idempotencia verificada)
        Assert.Equal(conteoTrasPrimera, conteoTrasSegunda);
    }

    // --- Helpers ---

    private async Task<int> ContarArticulosTotalesAsync()
    {
        var count = 0;
        await WithDbAsync(async db =>
        {
            count = await db.Articulos.CountAsync();
        });
        return count;
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
