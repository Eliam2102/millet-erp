#:project ../../backend/src/Integraciones.Aw/Millet.Integraciones.Aw.csproj
#:property PublishAot=false
// Copia una BD de A+W (SQL Server: sandbox AW_DEMO o una copia) a las tablas aw_origen.* del origen de demo PostgreSQL.
// Los productos se leen con el mismo lector que la sincronización (AwProductosSqlOrigen), así variantes, clasificación y
// árbol de piezas quedan idénticos a lo que el ERP ya sincronizó desde A+W. Reemplaza todo el contenido de aw_origen.*;
// la cola de pedidos (dbo.*) no se toca.
// Uso: dotnet run importar-desde-aw.cs -- "<cadena SQL Server>" "<cadena PostgreSQL>"   (lo invoca aw-origen-demo.sh importar)
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Productos;
using Npgsql;

if (args.Length != 2) { Console.Error.WriteLine("uso: importar-desde-aw.cs <cadena SQL Server> <cadena PostgreSQL>"); return 2; }
var (sqlCs, pgCs) = (args[0], args[1]);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

await using var sql = new SqlConnection(sqlCs);
await sql.OpenAsync();
await using var pg = new NpgsqlConnection(pgCs);
await pg.OpenAsync();
await using var tx = await pg.BeginTransactionAsync();
await new NpgsqlCommand("TRUNCATE aw_origen.ku_kunden, aw_origen.ka_zahlbed, aw_origen.erp_articulo", pg, tx).ExecuteNonQueryAsync();

// Condiciones de pago y clientes: copia columna a columna (mismas columnas que KA_ZAHLBED / KU_KUNDEN).
var condiciones = await Copiar("SELECT BEZ, BRUTTOTAGE, NUMMER FROM SYSADM.KA_ZAHLBED",
    "INSERT INTO aw_origen.ka_zahlbed (bez, bruttotage, nummer) VALUES ($1, $2, $3)");
var clientes = await Copiar("""
    SELECT ID, MANDANT, NAME1, NAME2, NAME3, STRASSE, ORT, PLZ, PROVINZ, LAND, UST_ID, STEUERNUMMER, TLF1, TLF2, MAIL,
           ZAHLBED, WAEHRUNG, KREDIT_LIMIT, KREDIT_LIMIT1, KREDIT_LIMIT_NET, KZ_STATUS, KZ_GESPERRT, CAST(DATUM AS date),
           TRANSACTION_TIME
      FROM SYSADM.KU_KUNDEN WHERE ID > 0
    """, """
    INSERT INTO aw_origen.ku_kunden (id, mandant, name1, name2, name3, strasse, ort, plz, provinz, land, ust_id,
        steuernummer, tlf1, tlf2, mail, zahlbed, waehrung, kredit_limit, kredit_limit1, kredit_limit_net, kz_status,
        kz_gesperrt, datum, transaction_time)
    VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21,$22,$23,$24)
    """);

// Productos: sin TiposExcluidos, el filtro lo aplica la API al leer de aw_origen.
var lector = new AwProductosSqlOrigen(new Fabrica(sqlCs),
    Options.Create(new AwProductosOptions { SqlQueryTimeoutSeconds = 120 }), NullLogger<AwProductosSqlOrigen>.Instance);
var productos = 0;
string? cursor = null;
do
{
    var pagina = await lector.LeerPaginaAsync(cursor, AwProductosOptions.TamanoLoteMaximo, CancellationToken.None);
    foreach (var p in pagina.Filas)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO aw_origen.erp_articulo (producto_ref, descripcion, unidad_medida, baja, transaction_time,
                codigo_modelo, grupo, tipo, wgr, wgr_descripcion, variantes, componentes)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11::jsonb,$12::jsonb)
            """, pg, tx);
        foreach (var v in new object?[] { int.Parse(p.ProductoRef!, CultureInfo.InvariantCulture), p.Descripcion, p.UnidadMedida,
                     p.Baja, p.TransactionTime, p.CodigoModelo, p.Grupo, p.Tipo, p.Wgr, p.WgrDescripcion,
                     JsonSerializer.Serialize(p.Variantes, json), JsonSerializer.Serialize(p.Componentes ?? [], json) })
            cmd.Parameters.Add(new NpgsqlParameter { Value = v ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync();
        productos++;
    }
    cursor = pagina.SiguienteCursor;
} while (cursor is not null);

await tx.CommitAsync();
Console.WriteLine($"importado: {clientes} clientes, {condiciones} condiciones de pago, {productos} productos");
return 0;

async Task<int> Copiar(string select, string insert)
{
    await using var leer = new SqlCommand(select, sql) { CommandTimeout = 120 };
    var filas = new List<object?[]>();
    await using (var r = await leer.ExecuteReaderAsync())
        while (await r.ReadAsync())
        {
            var fila = new object?[r.FieldCount];
            for (var i = 0; i < r.FieldCount; i++) fila[i] = r.IsDBNull(i) ? null : r.GetValue(i);
            filas.Add(fila);
        }
    foreach (var fila in filas)
    {
        await using var cmd = new NpgsqlCommand(insert, pg, tx);
        foreach (var v in fila) cmd.Parameters.Add(new NpgsqlParameter { Value = v ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync();
    }
    return filas.Count;
}

sealed class Fabrica(string cs) : IIntegracionSqlConnectionFactory
{
    public DbConnection CreateConnection() => new SqlConnection(cs);
}
