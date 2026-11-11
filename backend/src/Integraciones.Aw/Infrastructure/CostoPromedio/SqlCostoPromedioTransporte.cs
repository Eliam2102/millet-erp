using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Millet.Integraciones.Aw.Infrastructure.Adapters;

namespace Millet.Integraciones.Aw.Infrastructure.CostoPromedio;

/// <summary>
/// Transporte SQL ERP → Almacenes sobre <c>dbo.erp_costo_promedio</c> en la base de la app de Almacenes (<c>AlmacenRQ_MP</c>;
/// pruebas <c>AlmacenRQ_MP_ENSAYO</c>), <c>ConnectionStrings:AlmacenesAppDb</c> con TLS verificado. Usuario
/// <c>millet_erp_integracion</c>: SELECT e INSERT, DENY UPDATE/DELETE. Contrato v9, aprobado por TI el 10-nov (NULL en lo que no aplica, fila de sucursal);
/// <b>sin probar contra la base real</b>. Solo corre con <c>Integraciones:CostoPromedio:Transporte=Sql</c>.
/// </summary>
public sealed class SqlCostoPromedioTransporte(IConfiguration configuration) : ICostoPromedioTransporte
{
    public const string ConnectionStringName = "AlmacenesAppDb";

    // Idempotente: el INSERT solo corre si esa llave + versión no existe. Tipo y línea pueden ser NULL (fila de sucursal, herraje).
    private const string SqlEscribir = """
        INSERT INTO dbo.erp_costo_promedio
            (articulo_aw, sucursal, almacen_tipo, linea, ancho_mm, alto_mm, costo_m2, costo_pieza, moneda, vigente_desde, [version])
        SELECT @articulo, @sucursal, @tipo, @linea, @ancho, @alto, @costoM2, @costoPieza, @moneda, @vigente, @version
         WHERE NOT EXISTS (SELECT 1 FROM dbo.erp_costo_promedio
                            WHERE articulo_aw = @articulo AND sucursal = @sucursal
                              AND (almacen_tipo = @tipo OR (almacen_tipo IS NULL AND @tipo IS NULL))
                              AND (linea = @linea OR (linea IS NULL AND @linea IS NULL))
                              AND ISNULL(ancho_mm, 0) = ISNULL(@ancho, 0) AND ISNULL(alto_mm, 0) = ISNULL(@alto, 0)
                              AND [version] = @version)
        """;

    public async Task EscribirAsync(CostoPromedioEscrito escrito, CancellationToken ct)
    {
        var cadena = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(cadena))
            throw new InvalidOperationException($"Falta ConnectionStrings:{ConnectionStringName} para el transporte SQL de erp_costo_promedio.");
        await using var cn = new SqlConnection(AwSqlTls.Validar(cadena, ConnectionStringName));
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = SqlEscribir;
        P(cmd, "@articulo", DbType.Int32, escrito.Fila.ArticuloAw);
        P(cmd, "@sucursal", DbType.String, escrito.Fila.Sucursal);
        P(cmd, "@tipo", DbType.String, escrito.Fila.AlmacenTipo);
        P(cmd, "@linea", DbType.String, escrito.Fila.Linea);
        P(cmd, "@ancho", DbType.Int32, escrito.Fila.AnchoMm);
        P(cmd, "@alto", DbType.Int32, escrito.Fila.AltoMm);
        P(cmd, "@costoM2", DbType.Decimal, escrito.Fila.CostoM2);
        P(cmd, "@costoPieza", DbType.Decimal, escrito.Fila.CostoPieza);
        P(cmd, "@moneda", DbType.String, escrito.Moneda);
        P(cmd, "@vigente", DbType.DateTime2, escrito.VigenteDesde.UtcDateTime); // sin zona, se guarda en UTC
        P(cmd, "@version", DbType.Int32, escrito.Version);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void P(DbCommand cmd, string nombre, DbType tipo, object? valor)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = nombre; p.DbType = tipo; p.Value = valor ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}
