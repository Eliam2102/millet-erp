using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Millet.Compras.Application.Integration.AlmCompra;
using Millet.Integraciones.Aw.Infrastructure.Adapters;

namespace Millet.Integraciones.Aw.Infrastructure.AlmCompra;

/// <summary>
/// Transporte SQL ERP → Almacenes sobre <c>dbo.alm_compra</c> en la base de la app de Almacenes (<c>AlmacenRQ_MP</c>; pruebas
/// <c>AlmacenRQ_MP_ENSAYO</c>), la misma de <c>alm_mensaje</c> (<c>ConnectionStrings:AlmacenesAppDb</c>, TLS verificado; NO usa
/// MILLET_INTEGRACION). DDL del contrato v8: id, mensaje_id, tipo, folio, fecha, contenido, fecha_escrito (default) y el acuse de
/// Almacenes (acuse_estado PROCESADO/DUPLICADO/ERROR, acuse_compra_id, acuse_texto, acuse_fecha). Usuario
/// <c>millet_erp_integracion</c>: SELECT e INSERT, DENY DELETE: el ERP nunca escribe el acuse. Las compras de un folio se aplican
/// en orden de id y Almacenes retiene las posteriores a un ERROR; el orden y la espera los lleva el procesador (un mensaje a la
/// vez por folio, hasta leer su acuse). <b>Sin probar contra la base real.</b> Solo corre con <c>Integraciones:AlmCompra:Transporte=Sql</c>.
/// </summary>
public sealed class SqlAlmCompraTransporte(IConfiguration configuration) : IAlmCompraTransporte
{
    public const string ConnectionStringName = "AlmacenesAppDb";
    /// <summary>v11: acuse_texto NVARCHAR(1000); el mismo tope que al escribir en alm_mensaje.</summary>
    public const int MaxTexto = 1000;

    // Idempotente: el INSERT solo corre si el mensaje_id no existe (reintento tras una falla a medias no duplica).
    // fecha y acuse_fecha van en hora local del servidor (Mérida), sin zona, como fecha_escrito (Jorge, 12-nov): la conversión
    // la hace el propio servidor con su diferencia contra UTC.
    private const string SqlEscribir = """
        INSERT INTO dbo.alm_compra (mensaje_id, tipo, folio, fecha, contenido)
        SELECT @id, @tipo, @folio, DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()), @fecha), @contenido
         WHERE NOT EXISTS (SELECT 1 FROM dbo.alm_compra WHERE mensaje_id = @id)
        """;
    private const string SqlAcuse = """
        SELECT TOP (1) acuse_estado, acuse_compra_id, acuse_texto,
               DATEADD(MINUTE, DATEDIFF(MINUTE, SYSDATETIME(), SYSUTCDATETIME()), acuse_fecha)
          FROM dbo.alm_compra WHERE mensaje_id = @id AND acuse_estado IS NOT NULL ORDER BY id DESC
        """;

    public async Task EscribirAsync(AlmCompraMensaje mensaje, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = SqlEscribir;
        P(cmd, "@id", DbType.String, mensaje.MensajeId);
        P(cmd, "@tipo", DbType.String, mensaje.Tipo);
        P(cmd, "@folio", DbType.String, mensaje.Referencia);
        P(cmd, "@fecha", DbType.DateTime2, mensaje.Fecha.UtcDateTime); // UTC; el SQL lo pasa a hora local
        P(cmd, "@contenido", DbType.String, mensaje.Sobre);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<AlmCompraAcuse?> LeerAcuseAsync(string mensajeId, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = SqlAcuse;
        P(cmd, "@id", DbType.String, mensajeId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var texto = r.IsDBNull(2) ? null : r.GetString(2);
        return new AlmCompraAcuse(r.GetString(0), r.IsDBNull(1) ? null : Convert.ToString(r.GetValue(1)),
            texto is { Length: > MaxTexto } ? texto[..MaxTexto] : texto,
            r.IsDBNull(3) ? null : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc)));
    }

    private async Task<DbConnection> AbrirAsync(CancellationToken ct)
    {
        var cadena = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(cadena))
            throw new InvalidOperationException($"Falta ConnectionStrings:{ConnectionStringName} para el transporte SQL de alm_compra.");
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
