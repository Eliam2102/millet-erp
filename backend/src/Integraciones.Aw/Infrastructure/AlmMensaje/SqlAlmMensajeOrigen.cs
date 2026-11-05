using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Integraciones.Aw.Infrastructure.Adapters;

namespace Millet.Integraciones.Aw.Infrastructure.AlmMensaje;

/// <summary>
/// Origen SQL sobre <c>dbo.alm_mensaje</c> (base de la app de Almacenes). <b>Sin probar contra la base real:</b> la tabla no
/// existe hasta que TI la cree (~13-nov) y los nombres de columna son SUPUESTOS tomados de su correo del 29-oct (la app
/// escribe secuencia, mensaje_id único, tipo, versión, fecha, referencia y el sobre JSON; el ERP escribe solo estado
/// RECIBIDO/DUPLICADO/ERROR, folio, error y fecha del acuse; ningún lado borra). Usuario del ERP: SELECT y UPDATE de las
/// columnas <c>acuse_*</c>. Solo corre con <c>Integraciones:AlmMensaje:Origen=Sql</c> y <c>ConnectionStrings:AlmacenesAppDb</c>
/// (TLS verificado); ajustar al DDL definitivo antes de encenderlo.
/// </summary>
public sealed class SqlAlmMensajeOrigen(IConfiguration configuration) : IAlmMensajeOrigen
{
    public const string ConnectionStringName = "AlmacenesAppDb";

    // Pendiente = sin acuse y sin un ERROR anterior en su referencia (la referencia espera a que alguien lo revise).
    private const string SqlPendientes = """
        SELECT TOP (@max) m.secuencia, m.mensaje_id, m.tipo, m.version, m.referencia, m.fecha, m.sobre
          FROM dbo.alm_mensaje m
         WHERE m.acuse_estado IS NULL
           AND (m.referencia IS NULL OR NOT EXISTS (
                 SELECT 1 FROM dbo.alm_mensaje e
                  WHERE e.referencia = m.referencia AND e.secuencia < m.secuencia AND e.acuse_estado = 'ERROR'))
         ORDER BY m.secuencia
        """;
    // Solo si sigue sin acuse: dos lectores del mismo renglón no se pisan.
    private const string SqlAcuse = """
        UPDATE dbo.alm_mensaje
           SET acuse_estado = @estado, acuse_folio = @folio, acuse_error = @error, acuse_fecha = SYSUTCDATETIME()
         WHERE secuencia = @sec AND acuse_estado IS NULL
        """;

    public async Task<IReadOnlyList<AlmMensajeEntrante>> LeerPendientesAsync(int max, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = SqlPendientes;
        P(cmd, "@max", DbType.Int32, max);
        var r = new List<AlmMensajeEntrante>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            r.Add(new AlmMensajeEntrante(Convert.ToInt64(rd.GetValue(0)), rd.GetString(1), rd.GetString(2), Convert.ToInt32(rd.GetValue(3)),
                rd.IsDBNull(4) ? null : rd.GetString(4), Fecha(rd.GetValue(5)), rd.GetString(6)));
        return r;
    }

    public async Task EscribirAcuseAsync(long secuencia, AcuseApp acuse, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = SqlAcuse;
        P(cmd, "@sec", DbType.Int64, secuencia);
        P(cmd, "@estado", DbType.String, acuse.Estado);
        P(cmd, "@folio", DbType.String, (object?)acuse.Folio ?? DBNull.Value);
        P(cmd, "@error", DbType.String, (object?)acuse.Error ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static DateTimeOffset Fecha(object v) => v switch
    {
        DateTimeOffset o => o,
        DateTime d => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)),
        _ => DateTimeOffset.UtcNow,
    };

    private async Task<DbConnection> AbrirAsync(CancellationToken ct)
    {
        var cadena = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(cadena))
            throw new InvalidOperationException($"Falta ConnectionStrings:{ConnectionStringName} para leer alm_mensaje.");
        var cn = new SqlConnection(AwSqlTls.Validar(cadena, ConnectionStringName));
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
