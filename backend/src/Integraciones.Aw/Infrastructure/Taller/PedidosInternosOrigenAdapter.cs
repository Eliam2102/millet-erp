using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Taller;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Infrastructure.Taller;

/// <summary>Lector de pedidos internos Serie 9000 del origen activo (Demo o Real). Nunca escribe en A+W.</summary>
public interface IPedidosInternosOrigenReader
{
    Task<string> OrigenActualAsync(CancellationToken ct);

    /// <summary>Posiciones de pedidos Serie 9000 con fecha del pedido &gt;= <paramref name="desde"/>, en orden de fecha, pedido y posición.</summary>
    Task<IReadOnlyList<PedidoInternoLineaAw>> LeerAsync(DateOnly desde, int max, CancellationToken ct);
}

/// <summary>
/// Lector SOLO SELECT de los pedidos internos Serie 9000 (folios 90000000–99999999, hito 119). El aviso 15/42 los excluye
/// a propósito (#114), así que no pasan por <c>aw_solicitud_pedido</c> ni llegan a Facturación: se leen directo de las
/// mismas vistas de pedido (<c>vw_erp_pedido_cabecera/linea/componente</c>) que ya publica la integración. La medida de
/// la hoja sale del componente de la posición (mm). Demo en PostgreSQL; Real por la conexión de integración.
/// ponytail: la forma real del pedido 9000 en A+W está pedida a Millet (A+W-QA-8); si trae la hoja de otra manera, solo cambia este SELECT.
/// </summary>
public sealed class PedidosInternosOrigenAdapter(Func<DbConnection> fabrica, bool postgres) : IPedidosInternosOrigenReader
{
    internal const string Sql =
        "SELECT c.numero_pedido, l.numero_posicion, l.producto_ref, l.cantidad, c.numero_sucursal, c.fecha_transaccion, m.ancho_mm, m.alto_mm " +
        "FROM dbo.vw_erp_pedido_cabecera c " +
        "JOIN dbo.vw_erp_pedido_linea l ON l.numero_pedido = c.numero_pedido " +
        "LEFT JOIN (SELECT numero_pedido, numero_posicion, MAX(ancho_mm) AS ancho_mm, MAX(alto_mm) AS alto_mm " +
        "FROM dbo.vw_erp_pedido_componente GROUP BY numero_pedido, numero_posicion) m " +
        "ON m.numero_pedido = l.numero_pedido AND m.numero_posicion = l.numero_posicion " +
        "WHERE LEN(c.numero_pedido) = 8 AND c.numero_pedido >= '90000000' AND c.numero_pedido <= '99999999' AND c.fecha_transaccion >= @desde " +
        "ORDER BY c.fecha_transaccion, c.numero_pedido, l.numero_posicion";

    public Task<string> OrigenActualAsync(CancellationToken ct) => Task.FromResult(postgres ? "Demo" : "Real");

    public async Task<IReadOnlyList<PedidoInternoLineaAw>> LeerAsync(DateOnly desde, int max, CancellationToken ct)
    {
        await using var c = fabrica();
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = postgres ? Sql.Replace("LEN(", "length(") : Sql;
        var p = cmd.CreateParameter(); p.ParameterName = "desde"; p.DbType = DbType.Date; p.Value = desde.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add(p);

        var filas = new List<PedidoInternoLineaAw>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct) && filas.Count < max)
        {
            object? V(string n) { var i = r.GetOrdinal(n); return r.IsDBNull(i) ? null : r.GetValue(i); }
            string? Texto(string n) => V(n) is { } v ? Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() : null;
            decimal? Dec(string n) => V(n) is { } v ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : null;
            filas.Add(new(Texto("numero_pedido")!, Convert.ToInt32(V("numero_posicion"), CultureInfo.InvariantCulture), Texto("producto_ref") ?? "",
                Dec("cantidad") ?? 0, Dec("ancho_mm"), Dec("alto_mm"), Texto("numero_sucursal"),
                // Npgsql entrega date como DateOnly; SQL Server, como DateTime.
                V("fecha_transaccion") is DateOnly dia ? dia : DateOnly.FromDateTime(Convert.ToDateTime(V("fecha_transaccion"), CultureInfo.InvariantCulture))));
        }
        return filas;
    }
}

/// <summary>Elige el lector Demo o Real según el origen activo de A+W (mismo criterio que PT y A4.8).</summary>
public sealed class PedidosInternosOrigenSelector(IServiceProvider services, AwOrigenSesion sesion) : IPedidosInternosOrigenReader
{
    public async Task<string> OrigenActualAsync(CancellationToken ct) => await sesion.EsDemoAsync(ct) ? "Demo" : "Real";

    public async Task<IReadOnlyList<PedidoInternoLineaAw>> LeerAsync(DateOnly desde, int max, CancellationToken ct) =>
        await (services.GetKeyedService<IPedidosInternosOrigenReader>(await OrigenActualAsync(ct))
            ?? throw new BusinessRuleException("SERIE9000_ORIGEN_NO_HABILITADO",
                "Use Demo mientras no esté firmado I13.4; revise la conexión de integración y la bandera de Serie 9000."))
            .LeerAsync(desde, max, ct);
}
