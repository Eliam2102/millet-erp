using System.Data;
using System.Data.Common;
using System.Globalization;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Infrastructure.Exportacion;
/// <summary>
/// Lector de las tablas de envío OTR (contrato 09). Con <paramref name="yaLeida"/> nulo es el origen Demo, que marca el resultado en
/// sus columnas <c>erp_*</c>. Con <paramref name="yaLeida"/> es el lector Real: sólo SELECT, nunca escribe en A+W; el ERP decide qué
/// envío y versión ya leyó con su propio control.
/// </summary>
public sealed class OtrTablasAdapter(Func<DbConnection> fabrica, bool postgres, Func<CancellationToken, Task<Func<string, long, bool>>>? yaLeida = null, OtrOptions? opciones = null) : IOtrEntradaReader
{
    // Real (SQL Server 2016, misma instancia que MILMAIN: nombres de tres partes, sin OPENQUERY). Un envío = una pirámide (ROUTE_ID + CONTAINER_ID).
    // Mismas columnas y orden que dbo.aw_otr_envio, así el lector de filas es uno solo. Versión = máx. DATETIME_UPDATE de ruta, pirámide y posiciones en ms Unix.
    // Los pedidos se resuelven por DOCUMENT_ID = BW_AUFTR_KOPF.ID; sin pedido (o con estatus >= tope) la posición no cuenta. Sólo SELECT.
    internal static string CabeceraReal(OtrOptions o) =>
        "SELECT CONCAT(CONVERT(varchar(20), g.ROUTE_ID), '-', g.CONTAINER_ID), DATEDIFF_BIG(MILLISECOND, '1970-01-01', V.Ver), CONVERT(nvarchar(50), g.Cli), ISNULL(g.Suc, ''), " +
        "ISNULL(A.LOCATION_NAME1, CONVERT(nvarchar(50), g.Cli)), ISNULL(ISNULL(A.LOCATION_ADDRESS_TEXT, A.LOCATION_ADDRESS), ''), NULL, g.CONTAINER_ID, R.ROUTE_NAME, " +
        "CONVERT(int, ROUND(g.Piezas, 0)), g.Peso, R.DELIVERY_DATE, NULL, NULL, " +
        $"CASE WHEN g.NCli > 1 THEN 'CE_OTR_VARIOS_CLIENTES' WHEN g.NSuc > 1 THEN 'CE_OTR_VARIAS_SUCURSALES' WHEN R.ROUTE_STATUS IN ({Lista(o.EstatusRutaCancelada, true)}) THEN 'Cancelacion' ELSE 'Alta' END " +
        "FROM (SELECT P.ROUTE_ID, P.CONTAINER_ID, COUNT(DISTINCT K.AH_IDENT) NCli, COUNT(DISTINCT K.OR_AVBEREICH) NSuc, MIN(K.AH_IDENT) Cli, MIN(K.OR_AVBEREICH) Suc, SUM(P.POS_QTY) Piezas, SUM(P.POS_TOTAL_WEIGHT) Peso, MAX(P.DATETIME_UPDATE) Upd " +
        "FROM OTR.SYSADM.OTR_CONTAINER_POSITIONS P JOIN MILMAIN.SYSADM.BW_AUFTR_KOPF K ON K.ID = TRY_CONVERT(int, P.DOCUMENT_ID) AND K.STATUS < @estatusPedido GROUP BY P.ROUTE_ID, P.CONTAINER_ID) g " +
        "JOIN OTR.SYSADM.OTR_ROUTES R ON R.ROUTE_ID = g.ROUTE_ID " +
        "OUTER APPLY (SELECT TOP 1 X.LOCATION_NAME1, X.LOCATION_ADDRESS_TEXT, X.LOCATION_ADDRESS, X.DATETIME_UPDATE FROM OTR.SYSADM.OTR_CONTAINERS_ACTIONS X WHERE X.ROUTE_ID = g.ROUTE_ID AND X.CONTAINER_ID = g.CONTAINER_ID ORDER BY X.DATETIME_UPDATE DESC) A " +
        "CROSS APPLY (SELECT MAX(T.V) Ver FROM (VALUES (g.Upd), (A.DATETIME_UPDATE), (R.DATETIME_UPDATE)) T(V)) V " +
        $"WHERE R.ROUTE_STATUS NOT IN ({Lista(o.EstatusRutaExcluidos, false)}) AND (@entregaDesde IS NULL OR R.DELIVERY_DATE >= @entregaDesde) ORDER BY g.ROUTE_ID, g.CONTAINER_ID";
    internal const string LineasReal =
        "SELECT CONVERT(nvarchar(50), P.DOCUMENT_ID), P.POS_NR, SUM(P.POS_QTY), @unidad FROM OTR.SYSADM.OTR_CONTAINER_POSITIONS P JOIN MILMAIN.SYSADM.BW_AUFTR_KOPF K ON K.ID = TRY_CONVERT(int, P.DOCUMENT_ID) AND K.STATUS < @estatusPedido " +
        "WHERE P.ROUTE_ID = @ruta AND P.CONTAINER_ID = @contenedor GROUP BY P.DOCUMENT_ID, P.POS_NR ORDER BY P.DOCUMENT_ID, P.POS_NR";
    // Los estatus ya vienen validados como enteros (OtrOptions); un conjunto vacío no coincide con nada.
    private static string Lista(string texto, bool coincide) => !OtrOptions.Lista(texto, out var estatus) || estatus.Length == 0 ? (coincide ? "NULL" : "-1") : string.Join(",", estatus);
    private async Task<DbConnection> AbrirAsync(CancellationToken ct)
    {
        var c = fabrica();
        try { await c.OpenAsync(ct); return c; } catch { await c.DisposeAsync(); throw; }
    }
    private static DbCommand Comando(DbConnection c, string sql, params (string Nombre, object? Valor)[] parametros)
    {
        var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 30;
        foreach (var (nombre, valor) in parametros)
        { var p = cmd.CreateParameter(); p.ParameterName = nombre; p.Value = valor ?? DBNull.Value; cmd.Parameters.Add(p); }
        return cmd;
    }
    public async Task<IReadOnlyList<OtrEntrada>> LeerPendientesAsync(int max, CancellationToken ct)
    {
        var leida = yaLeida is null ? null : await yaLeida(ct);
        await using var c = await AbrirAsync(ct);
        // Real: sin TOP ni filtro erp_*; se filtra por lo ya leído en el ERP y se corta en max (ponytail: lee todas las cabeceras, bastan miles de filas).
        var top = postgres || leida is not null ? "" : "TOP (@max)"; var limit = postgres && leida is null ? "LIMIT @max" : "";
        var filtro = leida is null ? "WHERE (erp_resultado IS NULL OR erp_resultado = 'Error')" : "";
        var o = opciones ?? new OtrOptions();
        // Con opciones es el lector de las tablas de OTR (T-SQL); sin ellas, el lector Real usa la forma del contrato (simulador PostgreSQL de las pruebas de integración).
        var tablasOtr = leida is not null && opciones is not null;
        await using var cmd = !tablasOtr
            ? Comando(c, $"SELECT {top} otr_envio_id, version, cliente_numero_aw, sucursal_clave_aw, cliente, direccion_entrega, producto, numero_contenedor, ruta, bultos, peso_kg, fecha_carga_prevista, booking, bl_guia, operacion FROM dbo.aw_otr_envio {filtro} ORDER BY otr_envio_id, version {limit}", ("max", max))
            : Comando(c, CabeceraReal(o), ("estatusPedido", o.EstatusPedidoMenorA), ("entregaDesde", o.EntregaDesde?.ToDateTime(TimeOnly.MinValue)));
        var cabeceras = new List<OtrEntrada>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetString(0); var version = reader.GetInt64(1);
                if (leida?.Invoke(id, version) == true) continue;
                if (leida is not null && cabeceras.Count >= max) break;
                try
                {
                    string? Texto(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
                    var fecha = reader.IsDBNull(11) ? (DateOnly?)null : reader.GetValue(11) is DateOnly date ? date : DateOnly.FromDateTime(Convert.ToDateTime(reader.GetValue(11), CultureInfo.InvariantCulture));
                    // Npgsql devuelve DateOnly por GetFieldValue; SQL Server devuelve DateTime.
                    if (!reader.IsDBNull(11) && reader.GetValue(11) is DateOnly dia) fecha = dia;
                    var datos = new DatosContenedor(Texto(7), null, Texto(12), Texto(13), Texto(8), fecha,
                        reader.IsDBNull(10) ? null : reader.GetDecimal(10), reader.IsDBNull(9) ? null : reader.GetInt32(9));
                    var op = Texto(14);
                    if (op?.StartsWith("CE_OTR_", StringComparison.Ordinal) == true) throw new BusinessRuleException(op, "El envío OTR no se puede asignar a un solo cliente o sucursal.");
                    if (op is not ("Alta" or "Modificacion" or "Cancelacion")) throw new BusinessRuleException("CE_OTR_OPERACION_INVALIDA", "Operación OTR desconocida.");
                    cabeceras.Add(new(id, version, new(Guid.Empty, Guid.Empty, reader.GetString(4), reader.GetString(5), Texto(6), datos, [], op == "Cancelacion", reader.GetString(2), reader.GetString(3))));
                }
                catch (Exception ex)
                {
                    cabeceras.Add(new(id, version, new(Guid.Empty, Guid.Empty, "", "", null, new(null, null, null, null, null, null, null, null), []), ex is BusinessRuleException regla ? regla.Code : ex.Message, ex is not BusinessRuleException));
                }
            }
        }
        var result = new List<OtrEntrada>();
        foreach (var h in cabeceras)
        {
            if (h.ErrorLectura is not null) { result.Add(h); continue; }
            try
            {
                await using var lineas = !tablasOtr
                    ? Comando(c, "SELECT numero_pedido, posicion, cantidad, unidad FROM dbo.aw_otr_envio_linea WHERE otr_envio_id=@id AND version=@version ORDER BY numero_pedido, posicion", ("id", h.IdOrigen), ("version", h.Version))
                    : Comando(c, LineasReal, ("ruta", int.Parse(h.IdOrigen[..h.IdOrigen.IndexOf('-')], CultureInfo.InvariantCulture)), ("contenedor", h.IdOrigen[(h.IdOrigen.IndexOf('-') + 1)..]), ("estatusPedido", o.EstatusPedidoMenorA), ("unidad", o.UnidadLinea));
                var recibidas = new List<OtrLineaRecibida>();
                await using var reader = await lineas.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) recibidas.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetDecimal(2), reader.GetString(3)));
                result.Add(h with { Contenido = h.Contenido with { Lineas = recibidas } });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { result.Add(h with { ErrorLectura = ex.Message, ErrorTecnico = true }); }
        }
        return result;
    }
    public async Task MarcarResultadoAsync(string idOrigen, long version, string resultado, string? motivo, CancellationToken ct)
    {
        if (yaLeida is not null) return; // Real: A+W es sólo lectura.
        await using var c = await AbrirAsync(ct);
        await using var cmd = Comando(c, "UPDATE dbo.aw_otr_envio SET erp_resultado=@resultado, erp_motivo=@motivo, erp_procesada_at=@ahora WHERE otr_envio_id=@id AND version=@version",
            ("id", idOrigen), ("version", version), ("resultado", resultado), ("motivo", motivo), ("ahora", DateTimeOffset.UtcNow));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
