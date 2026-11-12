using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Integraciones.Aw.Infrastructure.Adapters;

namespace Millet.Integraciones.Aw.Infrastructure.AlmMensaje;

/// <summary>
/// Origen SQL de <c>dbo.alm_carga_inicial</c> (contrato v9 §4, base de la app <c>AlmacenRQ_MP</c>; pruebas <c>AlmacenRQ_MP_ENSAYO</c>).
/// Usuario <c>millet_erp_integracion</c>: SELECT y UPDATE solo de acuse_estado/texto/fecha; DENY DELETE. Misma cadena que
/// <see cref="SqlAlmMensajeOrigen"/>. <b>Sin probar contra la base real.</b> Solo corre con <c>Origen=Sql</c> y
/// <c>CargaInicialHabilitada=true</c>.
/// </summary>
public sealed class SqlAlmCargaInicialOrigen(IConfiguration configuration) : IAlmCargaInicialOrigen
{
    private const int MaxTexto = 1000;

    private const string SqlPendientes = """
        SELECT TOP (@max) carga_id, renglon, sucursal, tipo_almacen, linea, articulo_aw, articulo_almacen_aw, ancho, alto,
               lote, pedimento, etiqueta, piezas, m2, costo_m2, costo_pieza, moneda, fecha_corte
          FROM dbo.alm_carga_inicial
         WHERE carga_id = @carga AND acuse_estado IS NULL
         ORDER BY renglon
        """;
    // Solo si sigue sin acuse: dos lectores del mismo renglón no se pisan. acuse_fecha en hora local del servidor (Mérida, UTC-6),
    // sin zona, como fecha y fecha_escrito (Jorge, 12-nov: así se comparan las fechas de un renglón).
    private const string SqlAcuse = """
        UPDATE dbo.alm_carga_inicial
           SET acuse_estado = @estado, acuse_texto = @texto, acuse_fecha = SYSDATETIME()
         WHERE carga_id = @carga AND renglon = @renglon AND acuse_estado IS NULL
        """;

    public Task<int?> UltimaCargaAsync(CancellationToken ct) => EscalarAsync("SELECT MAX(carga_id) FROM dbo.alm_carga_inicial", ct);

    public Task<int?> CargaAplicadaAsync(CancellationToken ct) =>
        EscalarAsync("SELECT MAX(carga_id) FROM dbo.alm_carga_inicial WHERE acuse_estado = 'APLICADO'", ct);

    public async Task<IReadOnlyList<AlmCargaInicialRenglon>> LeerPendientesAsync(int cargaId, int max, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = SqlPendientes;
        P(cmd, "@max", DbType.Int32, max);
        P(cmd, "@carga", DbType.Int32, cargaId);
        var r = new List<AlmCargaInicialRenglon>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            r.Add(new AlmCargaInicialRenglon(
                Convert.ToInt32(rd.GetValue(0)), Convert.ToInt32(rd.GetValue(1)), Txt(rd, 2) ?? "",
                Txt(rd, 3), Txt(rd, 4), Convert.ToInt32(rd.GetValue(5)), rd.IsDBNull(6) ? null : Convert.ToInt32(rd.GetValue(6)),
                Dec(rd, 7), Dec(rd, 8), Txt(rd, 9), Txt(rd, 10), Txt(rd, 11), Convert.ToInt32(rd.GetValue(12)), Dec(rd, 13),
                Dec(rd, 14), Dec(rd, 15), Txt(rd, 16), rd.IsDBNull(17) ? null : Convert.ToDateTime(rd.GetValue(17))));
        return r;
    }

    public async Task EscribirAcuseAsync(int cargaId, int renglon, string estado, string? texto, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = SqlAcuse;
        P(cmd, "@carga", DbType.Int32, cargaId);
        P(cmd, "@renglon", DbType.Int32, renglon);
        P(cmd, "@estado", DbType.String, estado);
        P(cmd, "@texto", DbType.String, texto is null ? DBNull.Value : texto.Length > MaxTexto ? texto[..MaxTexto] : texto);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<int?> EscalarAsync(string sql, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is null or DBNull ? null : Convert.ToInt32(v);
    }

    // Vacío = null (la línea va vacía en herraje).
    private static string? Txt(DbDataReader rd, int i) =>
        rd.IsDBNull(i) ? null : Convert.ToString(rd.GetValue(i))?.Trim() is { Length: > 0 } t ? t : null;

    private static decimal? Dec(DbDataReader rd, int i) => rd.IsDBNull(i) ? null : Convert.ToDecimal(rd.GetValue(i));

    private async Task<DbConnection> AbrirAsync(CancellationToken ct)
    {
        var cadena = configuration.GetConnectionString(SqlAlmMensajeOrigen.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(cadena))
            throw new InvalidOperationException($"Falta ConnectionStrings:{SqlAlmMensajeOrigen.ConnectionStringName} para leer alm_carga_inicial.");
        var cn = new SqlConnection(AwSqlTls.Validar(cadena, SqlAlmMensajeOrigen.ConnectionStringName));
        await cn.OpenAsync(ct);
        return cn;
    }

    private static void P(DbCommand cmd, string nombre, DbType tipo, object valor)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = nombre; p.DbType = tipo; p.Value = valor;
        cmd.Parameters.Add(p);
    }
}
