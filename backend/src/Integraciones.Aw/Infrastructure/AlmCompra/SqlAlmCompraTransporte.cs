using System.Data;
using System.Data.Common;
using Millet.Compras.Application.Integration.AlmCompra;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.AlmCompra;

/// <summary>
/// Transporte SQL ERP → Almacenes sobre <c>dbo.alm_compra</c> (base MILLET_INTEGRACION). <b>Sin probar contra la base real:</b> la
/// tabla no existe hasta que Jorge la cree (~13-nov) y los nombres de columna son SUPUESTOS tomados de su correo del 29-oct
/// (ERP escribe mensaje_id único, tipo, versión, fecha, folio de compra y el sobre JSON; Almacenes escribe estado
/// PROCESADO/DUPLICADO/ERROR, id de compra, error y fecha). Solo corre con <c>Integraciones:AlmCompra:Transporte=Sql</c>
/// y <c>ConnectionStrings:AwIntegracionDb</c>; ajustar al DDL definitivo antes de encenderlo.
/// </summary>
public sealed class SqlAlmCompraTransporte(IServiceProvider servicios) : IAlmCompraTransporte
{
    // Idempotente: el INSERT solo corre si el mensaje_id no existe (y la UNIQUE de la tabla es la red de seguridad).
    private const string SqlEscribir = """
        INSERT INTO dbo.alm_compra (mensaje_id, tipo, version, fecha, folio_compra, sobre)
        SELECT @id, @tipo, @version, @fecha, @folio, @sobre
         WHERE NOT EXISTS (SELECT 1 FROM dbo.alm_compra WHERE mensaje_id = @id)
        """;
    private const string SqlAcuse = """
        SELECT acuse_estado, acuse_compra_id, acuse_error, acuse_fecha
          FROM dbo.alm_compra WHERE mensaje_id = @id AND acuse_estado IS NOT NULL
        """;

    public async Task EscribirAsync(AlmCompraMensaje mensaje, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = SqlEscribir;
        P(cmd, "@id", DbType.String, mensaje.MensajeId);
        P(cmd, "@tipo", DbType.String, mensaje.Tipo);
        P(cmd, "@version", DbType.Int32, mensaje.Version);
        P(cmd, "@fecha", DbType.DateTimeOffset, mensaje.Fecha);
        P(cmd, "@folio", DbType.String, mensaje.Referencia);
        P(cmd, "@sobre", DbType.String, mensaje.Sobre);
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
        return new AlmCompraAcuse(r.GetString(0), r.IsDBNull(1) ? null : Convert.ToString(r.GetValue(1)),
            r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : new DateTimeOffset(r.GetDateTime(3), TimeSpan.Zero));
    }

    private async Task<DbConnection> AbrirAsync(CancellationToken ct)
    {
        var fabrica = servicios.GetService(typeof(IIntegracionSqlConnectionFactory)) as IIntegracionSqlConnectionFactory
            ?? throw new InvalidOperationException("Falta ConnectionStrings:AwIntegracionDb para el transporte SQL de alm_compra.");
        var cn = fabrica.CreateConnection();
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
