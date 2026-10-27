using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Npgsql;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// F1-MP-01 S2: la migración <c>AlmacenVarianteMedida</c> (PK sustituto del saldo + variante) no pierde ni altera
/// existencias ya registradas, y su Down restaura el trigger vigente. Usa una base propia desechable.
/// </summary>
public sealed class AlmacenVarianteMedidaMigracionTests
{
    private const string Anterior = "20261026163650_AlmacenCostoExpedientePorUbicacion";
    private const string Migracion = "20261026233414_AlmacenVarianteMedida";

    [Fact]
    public async Task Up_conserva_filas_cantidades_y_valor_y_Down_restaura_el_trigger()
    {
        var gate = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? throw new InvalidOperationException("Ejecutar tools/validate-integration-isolated.sh.");
        var nombre = "millet_mig_var_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(gate) { Database = "postgres", Pooling = false };
        var destino = new NpgsqlConnectionStringBuilder(gate) { Database = nombre, Pooling = false };
        await using var conexionAdmin = new NpgsqlConnection(admin.ConnectionString);
        await conexionAdmin.OpenAsync();
        await using (var crear = new NpgsqlCommand($"CREATE DATABASE \"{nombre}\"", conexionAdmin)) await crear.ExecuteNonQueryAsync();
        try
        {
            await using var db = new AlmacenDbContext(
                new DbContextOptionsBuilder<AlmacenDbContext>().UseNpgsql(destino.ConnectionString).UseSnakeCaseNamingConvention().Options,
                new SinEmpresa());
            var migrator = db.GetService<IMigrator>();
            // Las migraciones viejas de Almacén leen estas dos tablas de Compartido (sin filas basta).
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE SCHEMA IF NOT EXISTS compartido;
                CREATE TABLE compartido.empresas (id uuid PRIMARY KEY);
                CREATE TABLE compartido.parametros_globales (clave text, valor text);");

            await migrator.MigrateAsync(Anterior);
            await Sembrar(db);
            var antes = await Resumen(db);
            var triggerAntes = await Trigger(db);
            Assert.Equal(3, antes.Filas);

            await migrator.MigrateAsync(Migracion);
            Assert.Equal(antes, await Resumen(db));
            Assert.Equal(3, await Escalar<long>(db, "SELECT COUNT(DISTINCT id) FROM almacen.saldos_inventario WHERE producto_aw_variante_id IS NULL"));
            Assert.NotEqual(triggerAntes, await Trigger(db));

            // Con una fila por variante, Down se niega en vez de colapsar existencias.
            await db.Database.ExecuteSqlRawAsync(@"
                INSERT INTO almacen.saldos_inventario (ubicacion_id, sub_almacen_id, articulo_id, producto_aw_variante_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
                SELECT ubicacion_id, sub_almacen_id, articulo_id, gen_random_uuid(), 1, 1, NOW() FROM almacen.saldos_inventario LIMIT 1");
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync(Anterior));
            Assert.Contains("No se puede revertir AlmacenVarianteMedida", ex.ToString());
            await db.Database.ExecuteSqlRawAsync("DELETE FROM almacen.saldos_inventario WHERE producto_aw_variante_id IS NOT NULL");

            // Una sola fila con variante (sin duplicados por par) también la niega.
            await db.Database.ExecuteSqlRawAsync("UPDATE almacen.saldos_inventario SET producto_aw_variante_id = gen_random_uuid() WHERE id = (SELECT id FROM almacen.saldos_inventario LIMIT 1)");
            ex = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync(Anterior));
            Assert.Contains("No se puede revertir AlmacenVarianteMedida", ex.ToString());
            await db.Database.ExecuteSqlRawAsync("UPDATE almacen.saldos_inventario SET producto_aw_variante_id = NULL");

            // Una fila con lote (sin variante) también la niega: el Down perdería el lote.
            await db.Database.ExecuteSqlRawAsync("UPDATE almacen.saldos_inventario SET lote = 'L-1' WHERE id = (SELECT id FROM almacen.saldos_inventario LIMIT 1)");
            ex = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync(Anterior));
            Assert.Contains("No se puede revertir AlmacenVarianteMedida", ex.ToString());
            await db.Database.ExecuteSqlRawAsync("UPDATE almacen.saldos_inventario SET lote = NULL");

            await migrator.MigrateAsync(Anterior);
            Assert.Equal(antes, await Resumen(db));
            Assert.Equal(triggerAntes, await Trigger(db));
        }
        finally
        {
            await using var borrar = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{nombre}\" WITH (FORCE)", conexionAdmin);
            await borrar.ExecuteNonQueryAsync();
        }
    }

    private static async Task Sembrar(AlmacenDbContext db)
    {
        var almacen = Guid.NewGuid();
        var sub = Guid.NewGuid();
        var racks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
            VALUES ({almacen}, 'MIG', 'Migración', {Guid.NewGuid()}, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
            VALUES ({sub}, {almacen}, 'MIG', 'Sub', 0, 0, 0, NOW(), NOW())");
        foreach (var rack in racks)
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({rack}, {sub}, {rack.ToString()[..8]}, 'Rack', 0, false, 0, NOW(), NOW())");
        // Tres existencias con costo de 12 decimales (el valor generado cantidad × costo debe sobrevivir intacto).
        (Guid Rack, decimal Cantidad, decimal Costo)[] filas = [(racks[0], 12.5m, 177.792981234567m), (racks[0], 3m, 10m), (racks[1], 40m, 0.5m)];
        foreach (var (rack, cantidad, costo) in filas)
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.saldos_inventario (ubicacion_id, sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
                VALUES ({rack}, {sub}, {Guid.NewGuid()}, {cantidad}, {costo}, NOW())");
    }

    private static async Task<(long Filas, decimal Cantidad, decimal Valor)> Resumen(AlmacenDbContext db)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open) await cmd.Connection.OpenAsync();
        cmd.CommandText = "SELECT COUNT(*), SUM(cantidad), SUM(valor_inventario_mxn) FROM almacen.saldos_inventario";
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.GetInt64(0), r.GetDecimal(1), r.GetDecimal(2));
    }

    private static Task<string> Trigger(AlmacenDbContext db) =>
        Escalar<string>(db, "SELECT pg_get_functiondef('almacen.fn_movimientos_actualizar_saldo'::regproc)");

    private static async Task<T> Escalar<T>(AlmacenDbContext db, string sql)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open) await cmd.Connection.OpenAsync();
        cmd.CommandText = sql;
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private sealed class SinEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Nada();
        private sealed class Nada : IDisposable { public void Dispose() { } }
    }
}
