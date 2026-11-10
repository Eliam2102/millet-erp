using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Infrastructure.NotasCredito;

/// <summary>
/// Sección <c>IntegracionesAw:NotasCreditoLectura</c> (respuesta de TI del 5-nov). La columna del pedido original y el grupo de clientes internos
/// vienen verificados contra A+W pero quedan configurables.
/// </summary>
public sealed class AwNotasCreditoLecturaOptions
{
    public const string SectionName = "IntegracionesAw:NotasCreditoLectura";
    /// <summary>Apagado por defecto. Desde el 5-nov TI otorgó SELECT a millet_erp_lector_aw (conexión directa a MILMAIN, ConnectionStrings:AwClientesDb) sobre BW_GUTSCH_KOPF, _HIST, _POS y KA_FIRMA_MITARB; se enciende por ambiente (IntegracionesAw__NotasCreditoLectura__Habilitada=true).</summary>
    public bool Habilitada { get; set; }
    /// <summary>Ventana móvil: solo se leen las notas cuyo estatus 95 es de los últimos N días. Evita traer los ~27,500 históricos (de antes del ERP no hay factura que ligar).</summary>
    public int VentanaDias { get; set; } = 7;
    /// <summary>Tope de notas por lectura (las más antiguas primero).</summary>
    public int MaxPorLectura { get; set; } = 200;
    /// <summary>Columna de BW_GUTSCH_KOPF con el pedido original; verificado en A+W: AH_HAUPT_AUFTR = BW_AUFTR_KOPF.ID.</summary>
    public string ColumnaPedidoPadre { get; set; } = "AH_HAUPT_AUFTR";
    /// <summary>KU_KUNDEN.GRUPPE de los clientes internos de almacén (14 en A+W: ALMACEN CONKAL, CANCUN…): sus notas no se leen. Vacío = no se excluye ninguno.</summary>
    public string? GrupoClientesInternos { get; set; } = "MOVIMIENTO INTERNO";
}

/// <summary>
/// Nota de crédito de A+W lista para el ERP. A+W no guarda folio de factura ni UUID: solo el pedido y el número de nota.
/// <paramref name="EsRanura"/>: el importe es el de la ranura del pedido (BW_AUFTR_KOPF.KO_FALZ): el ERP ya emite esa NC al timbrar la factura.
/// </summary>
public sealed record NotaCreditoAwLeida(int NumeroNota, int NumeroPedido, int ClienteRef, decimal? Importe, DateTime FechaEstatus95,
    string? Vendedor = null, bool EsRanura = false, string Motivo = "Bonificacion");

/// <summary>
/// Lector SOLO SELECT de las notas de crédito de A+W (BW_GUTSCH_KOPF + BW_GUTSCH_HIST, unidas a BW_AUFTR_KOPF por el pedido original).
/// Filtros de TI: estatus 95; solo las que tienen pedido original (≠ 0); del historial el ÚLTIMO registro de esa transición
/// (por DATUM y luego ID; 4 notas pasaron dos veces por 95); sin los clientes internos de almacén (KU_KUNDEN.GRUPPE = 'MOVIMIENTO INTERNO').
/// El JOIN a BW_AUFTR_KOPF deja fuera las 327 notas cuyo pedido original ya no existe. El vendedor es KA_FIRMA_MITARB (BW_GUTSCH_KOPF.OR_BEARBEITER = ID; LEFT JOIN: sin él la nota igual se lee; nunca se selecciona PASSWORD).
/// SQL común a SQL Server (Real) y PostgreSQL (simulador).
/// </summary>
public sealed class AwNotasCreditoLectorAdapter(Func<DbConnection> fabrica, AwNotasCreditoLecturaOptions opciones)
{
    public const int EstatusNotaEmitida = 95;
    private static readonly Regex Identificador = new("^[A-Za-z][A-Za-z0-9_]{0,40}$", RegexOptions.Compiled);

    internal static string ConstruirSql(string columnaPedidoPadre, bool excluirInternos)
    {
        if (!Identificador.IsMatch(columnaPedidoPadre)) throw new ArgumentException("Columna de pedido padre inválida.", nameof(columnaPedidoPadre));
        var internos = excluirInternos ? " AND c.GRUPPE <> @grupo" : "";
        return $"""
            SELECT k.ID, k.{columnaPedidoPadre}, k.AH_IDENT, k.FI_BETR_BRUTTO, h.DATUM,
                   a.KO_FALZ AS PED_FALZ, k.OR_BEARBEITER, m.VORNAME, m.NAME
              FROM SYSADM.BW_GUTSCH_KOPF k
              JOIN SYSADM.BW_AUFTR_KOPF a ON a.ID = k.{columnaPedidoPadre}
              JOIN SYSADM.KU_KUNDEN c ON c.ID = k.AH_IDENT
              LEFT JOIN SYSADM.KA_FIRMA_MITARB m ON m.ID = k.OR_BEARBEITER
              JOIN SYSADM.BW_GUTSCH_HIST h ON h.REF_ID = k.ID AND h.STATUS_ID = @estatus
               AND NOT EXISTS (SELECT 1 FROM SYSADM.BW_GUTSCH_HIST x WHERE x.REF_ID = k.ID AND x.STATUS_ID = @estatus
                                AND (x.DATUM > h.DATUM OR (x.DATUM = h.DATUM AND x.ID > h.ID)))
             WHERE k.STATUS = @estatus AND k.{columnaPedidoPadre} <> 0{internos} AND h.DATUM >= @desde
             ORDER BY h.DATUM, k.ID
            """;
    }

    public async Task<IReadOnlyList<NotaCreditoAwLeida>> LeerAsync(DateTime desde, CancellationToken ct)
    {
        if (!opciones.Habilitada)
            throw new BusinessRuleException("AW_NC_LECTURA_APAGADA", "La lectura directa de notas de crédito de A+W está apagada (falta el permiso de TI).");

        await using var cn = fabrica();
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = ConstruirSql(opciones.ColumnaPedidoPadre, !string.IsNullOrWhiteSpace(opciones.GrupoClientesInternos));
        cmd.CommandTimeout = 30;
        void P(string n, DbType t, object v) { var p = cmd.CreateParameter(); p.ParameterName = n; p.DbType = t; p.Value = v; cmd.Parameters.Add(p); }
        P("@estatus", DbType.Int32, EstatusNotaEmitida);
        P("@desde", DbType.DateTime, desde);
        if (!string.IsNullOrWhiteSpace(opciones.GrupoClientesInternos)) P("@grupo", DbType.String, opciones.GrupoClientesInternos);

        var lista = new List<NotaCreditoAwLeida>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            lista.Add(Mapear(r));
        return lista;
    }

    /// <summary>Tolerancia de redondeo entre el importe de la NC y la ranura del pedido (ver <see cref="Millet.Facturacion.Domain.Ports.RanuraAw"/>).</summary>
    internal const decimal ToleranciaRanura = Millet.Facturacion.Domain.Ports.RanuraAw.Tolerancia;

    private static NotaCreditoAwLeida Mapear(DbDataReader r)
    {
        decimal? D(int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
        string? T(int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i))?.Trim() is { Length: > 0 } t ? t : null;
        var importe = D(3);
        var ranuraPedido = D(5) ?? 0m;
        var nombre = string.Join(' ', new[] { T(7), T(8) }.Where(x => x is not null));
        return new(Convert.ToInt32(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)), Convert.ToInt32(r.GetValue(2)), importe,
            Convert.ToDateTime(r.GetValue(4)),
            Vendedor: nombre.Length > 0 ? nombre : T(6),
            EsRanura: ranuraPedido > 0 && importe is { } imp && Math.Abs(imp - ranuraPedido) <= ToleranciaRanura,
            // A+W no distingue bonificación de devolución. Todo va como bonificación (relación 01, sin efecto en inventario) hasta que Contabilidad confirme cómo separarlas.
            Motivo: "Bonificacion");
    }
}
