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
public sealed class OtrTablasAdapter(Func<DbConnection> fabrica, bool postgres, Func<CancellationToken, Task<Func<string, long, bool>>>? yaLeida = null) : IOtrEntradaReader
{
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
        await using var cmd = Comando(c, $"SELECT {top} otr_envio_id, version, cliente_numero_aw, sucursal_clave_aw, cliente, direccion_entrega, producto, numero_contenedor, ruta, bultos, peso_kg, fecha_carga_prevista, booking, bl_guia, operacion FROM dbo.aw_otr_envio {filtro} ORDER BY otr_envio_id, version {limit}", ("max", max));
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
                await using var lineas = Comando(c, "SELECT numero_pedido, posicion, cantidad, unidad FROM dbo.aw_otr_envio_linea WHERE otr_envio_id=@id AND version=@version ORDER BY numero_pedido, posicion", ("id", h.IdOrigen), ("version", h.Version));
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
