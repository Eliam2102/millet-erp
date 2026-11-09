using System.Globalization;
using Millet.Facturacion.Domain.Comprobantes;

namespace Millet.Facturacion.Domain.ConciliacionSat;

/// <summary>Renglón del archivo de metadatos del SAT: un CFDI emitido como lo tiene el SAT.</summary>
public sealed record CfdiSatMetadata(
    string Uuid,
    string RfcEmisor,
    string RfcReceptor,
    string? NombreReceptor,
    DateTime FechaEmision,
    decimal Monto,
    string Efecto,
    bool Cancelado,
    DateTime? FechaCancelacion);

/// <summary>CFDI timbrado del ERP, reducido a lo que se concilia.</summary>
/// <param name="EnPeriodo">Timbrado dentro del periodo conciliado; solo esos cuentan como «faltante en el SAT».</param>
public sealed record CfdiErpConciliable(
    Guid ComprobanteId,
    string Uuid,
    string Folio,
    string RfcReceptor,
    TipoComprobante Tipo,
    decimal Total,
    EstadoTimbrado Estado,
    bool EnPeriodo);

public sealed record DiferenciaDetectada(
    string Uuid,
    TipoDiferenciaSat Tipo,
    Guid? ComprobanteId,
    string? Folio,
    string? RfcReceptor,
    string? ValorSat,
    string? ValorErp);

/// <summary>
/// Compara los CFDI emitidos que reporta el SAT contra los timbrados del ERP por
/// UUID (FAC-10). Cada aspecto distinto es una diferencia aparte: un CFDI puede
/// tener estatus y total distintos a la vez. El total se compara al centavo.
/// </summary>
public static class ComparadorConciliacionSat
{
    public const string Vigente = "Vigente";
    public const string Cancelado = "Cancelado";

    public static IReadOnlyList<DiferenciaDetectada> Comparar(
        IEnumerable<CfdiSatMetadata> sat, IEnumerable<CfdiErpConciliable> erp)
    {
        var erpPorUuid = new Dictionary<string, CfdiErpConciliable>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in erp)
            erpPorUuid.TryAdd(Normalizar(c.Uuid), c);

        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var diferencias = new List<DiferenciaDetectada>();
        foreach (var s in sat)
        {
            var uuid = Normalizar(s.Uuid);
            if (!vistos.Add(uuid)) continue;
            if (!erpPorUuid.TryGetValue(uuid, out var e))
            {
                diferencias.Add(new(uuid, TipoDiferenciaSat.FaltanteEnErp, null, null, s.RfcReceptor,
                    $"{EstatusSat(s)} · {Importe(s.Monto)} · {s.Efecto}", null));
                continue;
            }

            var estatusErp = EstatusErp(e.Estado);
            var satCancelado = s.Cancelado;
            // Cancelación pendiente en el ERP y vigente en el SAT: el trámite sigue en curso, no es diferencia.
            var erpCancelado = e.Estado == EstadoTimbrado.Cancelado;
            if (satCancelado != erpCancelado)
                diferencias.Add(Con(e, uuid, TipoDiferenciaSat.EstatusDistinto, EstatusSat(s), estatusErp));
            if (decimal.Round(s.Monto, 2) != decimal.Round(e.Total, 2))
                diferencias.Add(Con(e, uuid, TipoDiferenciaSat.TotalDistinto, Importe(s.Monto), Importe(e.Total)));
            var efectoErp = Efecto(e.Tipo);
            if (!string.Equals(s.Efecto, efectoErp, StringComparison.OrdinalIgnoreCase))
                diferencias.Add(Con(e, uuid, TipoDiferenciaSat.TipoDistinto, s.Efecto, efectoErp));
            if (!string.Equals(s.RfcReceptor.Trim(), e.RfcReceptor.Trim(), StringComparison.OrdinalIgnoreCase))
                diferencias.Add(Con(e, uuid, TipoDiferenciaSat.ReceptorDistinto, s.RfcReceptor.Trim(), e.RfcReceptor.Trim()));
        }

        foreach (var e in erpPorUuid.Values.Where(e => e.EnPeriodo && !vistos.Contains(Normalizar(e.Uuid))))
            diferencias.Add(Con(e, Normalizar(e.Uuid), TipoDiferenciaSat.FaltanteEnSat, null,
                $"{EstatusErp(e.Estado)} · {Importe(e.Total)} · {Efecto(e.Tipo)}"));

        return diferencias;
    }

    /// <summary>Efecto del comprobante en el catálogo del SAT (c_TipoDeComprobante).</summary>
    public static string Efecto(TipoComprobante tipo) => tipo switch
    {
        TipoComprobante.Ingreso => "I",
        TipoComprobante.Egreso => "E",
        TipoComprobante.Traslado => "T",
        TipoComprobante.Pago => "P",
        _ => "?",
    };

    public static string Normalizar(string uuid) => uuid.Trim().ToUpperInvariant();

    private static string EstatusErp(EstadoTimbrado estado) => estado switch
    {
        EstadoTimbrado.Cancelado => Cancelado,
        EstadoTimbrado.CancelacionPendiente => "Vigente (cancelación pendiente)",
        _ => Vigente,
    };

    private static string EstatusSat(CfdiSatMetadata s) => s.Cancelado ? Cancelado : Vigente;

    private static string Importe(decimal v) => v.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static DiferenciaDetectada Con(CfdiErpConciliable e, string uuid, TipoDiferenciaSat tipo, string? sat, string? erp) =>
        new(uuid, tipo, e.ComprobanteId, e.Folio, e.RfcReceptor, sat, erp);
}
