using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Aw.Application.CostoPromedio;
using Millet.Integraciones.Aw.Infrastructure.Adapters;
using Millet.Integraciones.Aw.Infrastructure.AlmCompra;

namespace Millet.Integraciones.Aw.Infrastructure.CostoPromedio;

/// <summary>Destino en memoria (por omisión): solo desarrollo, demo y pruebas.</summary>
public sealed class SimuladoErpCostoPromedioDestino : IErpCostoPromedioDestino
{
    private readonly object _candado = new();
    private readonly List<ErpCostoPromedioFila> _filas = [];

    public IReadOnlyList<ErpCostoPromedioFila> Filas { get { lock (_candado) return _filas.ToList(); } }

    public Task<IReadOnlyList<ErpCostoPromedioFila>> LeerVigentesAsync(CancellationToken ct)
    {
        lock (_candado)
            return Task.FromResult<IReadOnlyList<ErpCostoPromedioFila>>(
                _filas.GroupBy(f => f.Llave).Select(g => g.MaxBy(f => f.Version)!).ToList());
    }

    public Task InsertarAsync(ErpCostoPromedioFila fila, CancellationToken ct)
    {
        lock (_candado)
            if (!_filas.Any(f => f.Llave == fila.Llave && f.Version == fila.Version)) _filas.Add(fila);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Destino SQL: <c>dbo.erp_costo_promedio</c> en la base de la app (<c>AlmacenRQ_MP</c>; pruebas <c>AlmacenRQ_MP_ENSAYO</c>), misma
/// cadena que <c>alm_compra</c> (<c>ConnectionStrings:AlmacenesAppDb</c>, TLS verificado). Usuario <c>millet_erp_integracion</c>:
/// SELECT + INSERT, DENY UPDATE/DELETE (script 19). <b>Sin probar contra la base real.</b> Solo corre con <c>Destino=Sql</c>.
/// </summary>
public sealed class SqlErpCostoPromedioDestino(IConfiguration configuration) : IErpCostoPromedioDestino
{
    public const string SqlVigentes = """
        SELECT articulo_aw, sucursal, almacen_tipo, linea, ancho_mm, alto_mm, costo_m2, costo_pieza, vigente_desde, version
          FROM dbo.vw_erp_costo_promedio_vigente
        """;

    // Idempotente por llave + versión; el índice único es la red si dos instancias insertan a la vez.
    public const string SqlInsertar = """
        INSERT INTO dbo.erp_costo_promedio
               (articulo_aw, sucursal, almacen_tipo, linea, ancho_mm, alto_mm, costo_m2, costo_pieza, moneda, vigente_desde, version)
        SELECT @articulo, @sucursal, @tipo, @linea, @ancho, @alto, @costoM2, @costoPieza, N'PESOSMX', @vigente, @version
         WHERE NOT EXISTS (SELECT 1 FROM dbo.erp_costo_promedio
                            WHERE articulo_aw = @articulo AND sucursal = @sucursal AND almacen_tipo = @tipo AND linea = @linea
                              AND (ancho_mm = @ancho OR (ancho_mm IS NULL AND @ancho IS NULL))
                              AND (alto_mm = @alto OR (alto_mm IS NULL AND @alto IS NULL))
                              AND version = @version)
        """;

    public async Task<IReadOnlyList<ErpCostoPromedioFila>> LeerVigentesAsync(CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = SqlVigentes;
        var filas = new List<ErpCostoPromedioFila>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            filas.Add(new ErpCostoPromedioFila(
                Convert.ToInt32(r.GetValue(0)), r.GetString(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : Convert.ToInt32(r.GetValue(4)), r.IsDBNull(5) ? null : Convert.ToInt32(r.GetValue(5)),
                r.IsDBNull(6) ? null : r.GetDecimal(6), r.IsDBNull(7) ? null : r.GetDecimal(7),
                new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(8), DateTimeKind.Utc)), Convert.ToInt64(r.GetValue(9))));
        return filas;
    }

    public async Task InsertarAsync(ErpCostoPromedioFila fila, CancellationToken ct)
    {
        var f = fila;
        await using var cn = await AbrirAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = SqlInsertar;
        P(cmd, "@articulo", DbType.Int32, f.ArticuloAw);
        P(cmd, "@sucursal", DbType.String, f.Sucursal);
        P(cmd, "@tipo", DbType.String, f.AlmacenTipo);
        P(cmd, "@linea", DbType.String, f.Linea);
        P(cmd, "@ancho", DbType.Int32, f.AnchoMm);
        P(cmd, "@alto", DbType.Int32, f.AltoMm);
        P(cmd, "@costoM2", DbType.Decimal, f.CostoM2);
        P(cmd, "@costoPieza", DbType.Decimal, f.CostoPieza);
        P(cmd, "@vigente", DbType.DateTime2, f.VigenteDesde.UtcDateTime); // datetime2: sin zona, en UTC
        P(cmd, "@version", DbType.Int64, f.Version);
        try { await cmd.ExecuteNonQueryAsync(ct); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { } // otra instancia ya la insertó
    }

    private async Task<DbConnection> AbrirAsync(CancellationToken ct)
    {
        var cadena = configuration.GetConnectionString(SqlAlmCompraTransporte.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(cadena))
            throw new InvalidOperationException($"Falta ConnectionStrings:{SqlAlmCompraTransporte.ConnectionStringName} para publicar erp_costo_promedio.");
        var cn = new SqlConnection(AwSqlTls.Validar(cadena, SqlAlmCompraTransporte.ConnectionStringName));
        await cn.OpenAsync(ct);
        return cn;
    }

    private static void P(DbCommand cmd, string nombre, DbType tipo, object? valor)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = nombre; p.DbType = tipo; p.Value = valor ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}

public static class ErpCostoPromedioDependencyInjection
{
    /// <summary>Publicador y destino de <c>erp_costo_promedio</c> (M-447): <c>Simulado</c> por omisión o <c>Sql</c>; worker apagado.</summary>
    public static IServiceCollection AddErpCostoPromedio(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ErpCostoPromedioOptions>().Bind(configuration.GetSection(ErpCostoPromedioOptions.SectionName));
        services.AddScoped<ErpCostoPromedioPublicador>();
        services.AddHostedService<ErpCostoPromedioWorker>();
        var destino = configuration[$"{ErpCostoPromedioOptions.SectionName}:{nameof(ErpCostoPromedioOptions.Destino)}"];
        if (string.Equals(destino, "Sql", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IErpCostoPromedioDestino, SqlErpCostoPromedioDestino>();
            return services;
        }
        if (!string.IsNullOrWhiteSpace(destino) && !string.Equals(destino, "Simulado", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Integraciones:CostoPromedio:Destino '{destino}' no válido (Simulado | Sql).");
        services.AddSingleton<SimuladoErpCostoPromedioDestino>();
        services.AddSingleton<IErpCostoPromedioDestino>(sp => sp.GetRequiredService<SimuladoErpCostoPromedioDestino>());
        return services;
    }
}
