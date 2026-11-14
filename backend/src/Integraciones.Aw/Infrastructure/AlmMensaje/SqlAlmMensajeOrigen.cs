using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Integraciones.Aw.Infrastructure.Adapters;

namespace Millet.Integraciones.Aw.Infrastructure.AlmMensaje;

/// <summary>
/// Origen SQL sobre <c>dbo.alm_mensaje</c> (base de la app de Almacenes, <c>AlmacenRQ_MP</c>; pruebas <c>AlmacenRQ_MP_ENSAYO</c>),
/// con el DDL del contrato v8 (8-nov): id, mensaje_id, intento, tipo, referencia, fecha, sucursal, tipo_almacen, linea,
/// contenido, fecha_escrito y acuse_*. Usuario <c>millet_erp_integracion</c>: SELECT y UPDATE solo de acuse_estado/folio/texto/fecha;
/// DENY DELETE. Columnas y tipos verificados contra ENSAYO en solo lectura (11-nov); los acuses se prueban el 13-nov. Solo corre con <c>Integraciones:AlmMensaje:Origen=Sql</c> y
/// <c>ConnectionStrings:AlmacenesAppDb</c> (TLS verificado).
/// Reenvío (v8): renglón NUEVO con el mismo mensaje_id e intento+1; vale el de intento más alto. El orden de una referencia es el
/// del primer renglón de cada mensaje, así un reenvío conserva su lugar; un ERROR frena a los siguientes de su referencia solo
/// mientras el ÚLTIMO intento de ese mensaje siga en ERROR.
/// </summary>
public sealed class SqlAlmMensajeOrigen(IConfiguration configuration) : IAlmMensajeOrigen
{
    public const string ConnectionStringName = "AlmacenesAppDb";
    private const int MaxTexto = 2000; // acuse_texto NVARCHAR(2000) (verificado en ENSAYO, 11-nov)

    // ponytail: agrupa toda la tabla cada 10 s; si crece a millones de renglones, acotar por fecha_escrito o indexar (mensaje_id, intento).
    private const string SqlPendientes = """
        WITH u AS (SELECT mensaje_id, MAX(intento) AS ultimo, MIN(id) AS primero FROM dbo.alm_mensaje GROUP BY mensaje_id)
        SELECT TOP (@max) m.id, m.mensaje_id, m.intento, m.tipo, m.referencia, m.fecha, m.contenido
          FROM dbo.alm_mensaje m
          JOIN u ON u.mensaje_id = m.mensaje_id AND u.ultimo = m.intento
         WHERE m.acuse_estado IS NULL
           AND (@prefijo IS NULL OR m.mensaje_id LIKE @prefijo + '%')
           AND (m.referencia IS NULL OR NOT EXISTS (
                 SELECT 1 FROM dbo.alm_mensaje e
                   JOIN u ue ON ue.mensaje_id = e.mensaje_id AND ue.ultimo = e.intento
                  WHERE e.referencia = m.referencia AND ue.primero < u.primero AND e.acuse_estado = 'ERROR'))
         ORDER BY u.primero
        """;
    // Solo si sigue sin acuse: dos lectores del mismo renglón no se pisan. acuse_fecha en hora local del servidor (Mérida, UTC-6),
    // sin zona, como fecha y fecha_escrito (Jorge, 12-nov: así se comparan las fechas de un renglón).
    private const string SqlAcuse = """
        UPDATE dbo.alm_mensaje
           SET acuse_estado = @estado, acuse_folio = @folio, acuse_texto = @texto, acuse_fecha = SYSDATETIME()
         WHERE id = @id AND acuse_estado IS NULL
        """;

    public async Task<IReadOnlyList<AlmMensajeEntrante>> LeerPendientesAsync(int max, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = SqlPendientes;
        P(cmd, "@max", DbType.Int32, max);
        P(cmd, "@prefijo", DbType.String, Prefijo());
        var r = new List<AlmMensajeEntrante>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            r.Add(new AlmMensajeEntrante(Convert.ToInt64(rd.GetValue(0)), rd.GetString(1), Convert.ToInt32(rd.GetValue(2)), rd.GetString(3),
                rd.IsDBNull(4) ? null : rd.GetString(4), Fecha(rd.GetValue(5)), rd.GetString(6)));
        return r;
    }

    // M-283: fecha_escrito es hora local de la base (verificado en ENSAYO, UTC-6), por eso se compara con SYSDATETIME() y no con UTC.
    private const string SqlSinAcuseAntiguos = """
        WITH u AS (SELECT mensaje_id, MAX(intento) AS ultimo FROM dbo.alm_mensaje GROUP BY mensaje_id)
        SELECT COUNT(*), ISNULL(MAX(DATEDIFF(SECOND, m.fecha_escrito, SYSDATETIME())), 0)
          FROM dbo.alm_mensaje m
          JOIN u ON u.mensaje_id = m.mensaje_id AND u.ultimo = m.intento
         WHERE m.acuse_estado IS NULL AND (@prefijo IS NULL OR m.mensaje_id LIKE @prefijo + '%')
           AND DATEDIFF(SECOND, m.fecha_escrito, SYSDATETIME()) > @segundos
        """;

    public async Task<AlmMensajeSinAcuse> ContarSinAcuseAntiguosAsync(int segundos, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = SqlSinAcuseAntiguos;
        P(cmd, "@segundos", DbType.Int32, segundos);
        P(cmd, "@prefijo", DbType.String, Prefijo());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        await rd.ReadAsync(ct);
        return new AlmMensajeSinAcuse(Convert.ToInt32(rd.GetValue(0)), Convert.ToInt32(rd.GetValue(1)));
    }

    public async Task EscribirAcuseAsync(long secuencia, AcuseApp acuse, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = SqlAcuse;
        P(cmd, "@id", DbType.Int64, secuencia);
        P(cmd, "@estado", DbType.String, acuse.Estado);
        P(cmd, "@folio", DbType.String, (object?)acuse.Folio ?? DBNull.Value);
        P(cmd, "@texto", DbType.String, acuse.Error is null ? DBNull.Value : acuse.Error.Length > MaxTexto ? acuse.Error[..MaxTexto] : acuse.Error);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// <c>Integraciones:AlmMensaje:SoloPrefijoMensajeId</c>: solo para pruebas en ENSAYO. Con valor, el lector (y la alarma) ven únicamente los
    /// mensajes cuyo <c>mensaje_id</c> empieza con él; vacío (por omisión) = todos.
    /// </summary>
    private object Prefijo() =>
        configuration["Integraciones:AlmMensaje:SoloPrefijoMensajeId"] is { Length: > 0 } p ? p : DBNull.Value;

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
