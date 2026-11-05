using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Infrastructure.NotasCredito;

/// <summary>
/// Sección <c>IntegracionesAw:NotasCreditoLectura</c> (respuesta de TI del 5-nov). Las dos cosas que TI aún no confirma se
/// configuran, no se adivinan: la columna de BW_GUTSCH_KOPF con el pedido original («pedido padre») y los clientes internos de almacén.
/// </summary>
public sealed class AwNotasCreditoLecturaOptions
{
    public const string SectionName = "IntegracionesAw:NotasCreditoLectura";
    /// <summary>Apagado por defecto: el usuario de solo lectura millet_erp_lector_aw (conexión directa a MILMAIN, ConnectionStrings:AwClientesDb) aún no tiene SELECT sobre BW_GUTSCH_KOPF, _HIST y _POS (los concede TI). El vendedor (KA_FIRMA_MITARB) no se lee: ese usuario no la tiene hoy.</summary>
    public bool Habilitada { get; set; }
    /// <summary>Columna de BW_GUTSCH_KOPF con el pedido original (candidata: AH_HAUPT_AUFTR, sin confirmar). Vacía = no se lee.</summary>
    public string? ColumnaPedidoPadre { get; set; }
    /// <summary>AH_IDENT de los clientes que son el almacén de una sucursal: sus notas no se leen. Vacío = no se excluye ninguno.</summary>
    public int[] ClientesAlmacenInternos { get; set; } = [];
}

/// <summary>Nota de crédito de A+W lista para el ERP. A+W no guarda folio de factura ni UUID: solo el pedido y el número de nota.</summary>
public sealed record NotaCreditoAwLeida(int NumeroNota, int NumeroPedido, int ClienteRef, decimal? Importe, DateTime FechaEstatus95);

/// <summary>
/// Lector SOLO SELECT de las notas de crédito de A+W (BW_GUTSCH_KOPF + BW_GUTSCH_HIST, unidas a BW_AUFTR_KOPF por el pedido original).
/// Filtros de TI: estatus 95; solo las que tienen pedido original (≠ 0); del historial el ÚLTIMO registro de esa transición
/// (4 notas pasaron dos veces por 95); sin los clientes internos de almacén. SQL común a SQL Server (Real) y PostgreSQL (simulador).
/// </summary>
public sealed class AwNotasCreditoLectorAdapter(Func<DbConnection> fabrica, AwNotasCreditoLecturaOptions opciones)
{
    public const int EstatusNotaEmitida = 95;
    private static readonly Regex Identificador = new("^[A-Za-z][A-Za-z0-9_]{0,40}$", RegexOptions.Compiled);

    internal static string ConstruirSql(string columnaPedidoPadre, int clientesAlmacen)
    {
        if (!Identificador.IsMatch(columnaPedidoPadre)) throw new ArgumentException("Columna de pedido padre inválida.", nameof(columnaPedidoPadre));
        var almacen = clientesAlmacen == 0 ? "" :
            $" AND k.AH_IDENT NOT IN ({string.Join(", ", Enumerable.Range(0, clientesAlmacen).Select(i => $"@alm{i}"))})";
        return $"""
            SELECT k.ID, k.{columnaPedidoPadre}, k.AH_IDENT, k.FI_BETR_BRUTTO, h.DATUM
              FROM SYSADM.BW_GUTSCH_KOPF k
              JOIN SYSADM.BW_AUFTR_KOPF a ON a.ID = k.{columnaPedidoPadre}
              JOIN SYSADM.BW_GUTSCH_HIST h ON h.REF_ID = k.ID AND h.STATUS_ID = @estatus
               AND h.ID = (SELECT MAX(x.ID) FROM SYSADM.BW_GUTSCH_HIST x WHERE x.REF_ID = k.ID AND x.STATUS_ID = @estatus)
             WHERE k.STATUS = @estatus AND k.{columnaPedidoPadre} <> 0{almacen} AND h.DATUM >= @desde
             ORDER BY h.DATUM, k.ID
            """;
    }

    public async Task<IReadOnlyList<NotaCreditoAwLeida>> LeerAsync(DateTime desde, CancellationToken ct)
    {
        if (!opciones.Habilitada)
            throw new BusinessRuleException("AW_NC_LECTURA_APAGADA", "La lectura directa de notas de crédito de A+W está apagada (falta el permiso de TI).");
        if (string.IsNullOrWhiteSpace(opciones.ColumnaPedidoPadre))
            throw new BusinessRuleException("AW_NC_COLUMNA_SIN_CONFIGURAR", "Falta configurar la columna del pedido original de BW_GUTSCH_KOPF (TI no la ha confirmado).");

        await using var cn = fabrica();
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = ConstruirSql(opciones.ColumnaPedidoPadre, opciones.ClientesAlmacenInternos.Length);
        cmd.CommandTimeout = 30;
        void P(string n, DbType t, object v) { var p = cmd.CreateParameter(); p.ParameterName = n; p.DbType = t; p.Value = v; cmd.Parameters.Add(p); }
        P("@estatus", DbType.Int32, EstatusNotaEmitida);
        P("@desde", DbType.DateTime, desde);
        for (var i = 0; i < opciones.ClientesAlmacenInternos.Length; i++) P($"@alm{i}", DbType.Int32, opciones.ClientesAlmacenInternos[i]);

        var lista = new List<NotaCreditoAwLeida>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            lista.Add(new(Convert.ToInt32(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)), Convert.ToInt32(r.GetValue(2)),
                r.IsDBNull(3) ? null : Convert.ToDecimal(r.GetValue(3)), Convert.ToDateTime(r.GetValue(4))));
        return lista;
    }
}
