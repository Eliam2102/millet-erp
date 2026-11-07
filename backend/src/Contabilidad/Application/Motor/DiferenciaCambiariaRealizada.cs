using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// CON-11 (86e3kdypd) · diferencia cambiaria realizada de pagos a proveedor (#5) y cobros de cliente (#12) en moneda extranjera
/// (Cuestionario 08, C-10; R14 del Módulo 10). El lado del documento (proveedor o cliente, IVA y retenciones) se asienta al TC con que
/// se registró la factura; el lado del dinero (bancos y ajustes del cobro) al TC del pago. Lo que separa ambos lo asienta el motor en
/// pérdida o utilidad cambiaria realizada (<see cref="EventoContable.CampoDiferenciaCambiaria"/>), al centavo.
/// <list type="bullet">
///   <item>TC de la factura: el del saldo de IVA del documento (C1.6, #483); si el documento no tiene saldo (sin IVA, o anterior a
///   C1.6), el de las partidas en esa moneda de su póliza; en el pago a proveedor, al final, el del pasivo que trae el evento.</item>
///   <item>TC del pago: <c>TipoCambioPago</c> del evento si Tesorería lo manda (TES-09); si no, el del catálogo K10.6 a la fecha del
///   pago (escalera FIX → DOF). Sin ninguno, el pago va a la bandeja por dato faltante y se reprocesa al cargar el TC.</item>
///   <item>Un cobro que salda facturas con distinto TC usa el TC ponderado de lo aplicado (el redondeo queda en la diferencia).</item>
///   <item>Pagos en MXN no cambian (TES-09-a).</item>
/// </list>
/// </summary>
public sealed class DiferenciaCambiariaRealizada(ContabilidadDbContext db, ITipoCambioContabilidadReadPort tiposCambio)
{
    /// <summary>Roles del lado del dinero, al TC del pago: el banco y los ajustes autorizados del cobro (CXC-06).</summary>
    public static readonly IReadOnlyList<string> RolesAlTcPago = ["BANCOS", "COMISIONES_BANCARIAS", "DIFERENCIAS_EN_COBRO"];

    public static bool Aplica(EventoContable e) =>
        e.Tipo is NormalizadorEventos.PagoProveedorAplicado or NormalizadorEventos.PagoClienteConfirmado && e.Moneda != Poliza.MonedaNacional;

    /// <summary>
    /// Pone el TC de la factura como TC del evento y el del pago en los roles del dinero. Lanza <see cref="RechazoMotorException"/> si
    /// no hay TC del pago.
    /// </summary>
    public async Task<EventoContable> AplicarAsync(EventoContable e, IReadOnlyList<ControlIvaDocumentos.Aplicacion> aplicaciones, CancellationToken ct)
    {
        if (!Aplica(e)) return e;
        var tcPago = TipoCambioPagoDelEvento(e.Payload) ?? await tiposCambio.ValorEnMxnAsync(e.Moneda, e.FechaContable, ct)
            ?? throw new RechazoMotorException(CausaRechazoContable.DatoFaltante, "TipoCambioPago",
                $"Falta el tipo de cambio de {e.Moneda} del {e.FechaContable:dd/MM/yyyy} en el catálogo (K10.6); cárgalo y reprocesa el pago desde la bandeja.");

        var documentos = new List<(decimal Importe, decimal Tc)>();
        foreach (var a in aplicaciones.Where(a => a.Importe > 0m))
        {
            var tc = await TipoCambioDocumentoAsync(e.EmpresaId, e.Moneda, a.Documento, a.DocumentoId, ct)
                ?? (e.Tipo == NormalizadorEventos.PagoProveedorAplicado && e.TipoCambio is > 0 ? e.TipoCambio.Value : tcPago);
            documentos.Add((a.Importe, tc));
        }
        // Lo que el cobro no aplica a una factura (saldo a favor por identificar) no tiene TC de documento: va al del pago.
        var aplicado = e.Tipo == NormalizadorEventos.PagoClienteConfirmado ? e.Importes.GetValueOrDefault("Aplicado") ?? 0m : 0m;
        var sinDocumento = aplicado - documentos.Sum(d => d.Importe);
        if (sinDocumento > 0m) documentos.Add((sinDocumento, tcPago));

        var tcDocumento = TipoCambioPonderado(documentos) ?? (e.Tipo == NormalizadorEventos.PagoProveedorAplicado && e.TipoCambio is > 0
            ? e.TipoCambio.Value : tcPago);
        return e with { TipoCambio = tcDocumento, TipoCambioPorRol = RolesAlTcPago.ToDictionary(r => r, _ => tcPago) };
    }

    /// <summary>TC de varios documentos: el común si todos coinciden; si no, pesos ÷ importe (6 decimales). Null sin documentos.</summary>
    internal static decimal? TipoCambioPonderado(IReadOnlyList<(decimal Importe, decimal Tc)> documentos)
    {
        if (documentos.Count == 0) return null;
        if (documentos.All(d => d.Tc == documentos[0].Tc)) return documentos[0].Tc;
        var importe = documentos.Sum(d => d.Importe);
        if (importe <= 0m) return null;
        var pesos = documentos.Sum(d => Math.Round(d.Importe * d.Tc, 2, MidpointRounding.AwayFromZero));
        return Math.Round(pesos / importe, 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>TC con que se contabilizó el documento: saldo de IVA (C1.6) o partidas de su póliza vigente en esa moneda.</summary>
    private async Task<decimal?> TipoCambioDocumentoAsync(Guid empresaId, string moneda, (string Modulo, string Tipo) doc, Guid documentoId,
        CancellationToken ct)
    {
        var saldo = await db.SaldosIvaDocumento.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.EmpresaId == empresaId && s.OrigenModulo == doc.Modulo && s.OrigenTipo == doc.Tipo && s.DocumentoId == documentoId)
            .Select(s => new { s.Moneda, s.TipoCambio }).FirstOrDefaultAsync(ct);
        if (saldo is { TipoCambio: > 0 } && saldo.Moneda == moneda) return saldo.TipoCambio;
        var tc = await db.Polizas.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.OrigenModulo == doc.Modulo && p.OrigenTipo == doc.Tipo && p.OrigenDocumentoId == documentoId
                && p.PolizaAnuladaId == null)
            .SelectMany(p => p.Partidas)
            .Where(x => x.Moneda == moneda && x.TipoCambio > 0m)
            .OrderBy(x => x.Linea)
            .Select(x => (decimal?)x.TipoCambio)
            .FirstOrDefaultAsync(ct);
        return tc;
    }

    /// <summary>TES-09 (aditivo): TC del pago que manda Tesorería; hoy no viaja y se toma del catálogo.</summary>
    internal static decimal? TipoCambioPagoDelEvento(string payload)
    {
        var tc = NormalizadorEventos.LeerContrato<TipoCambioPagoContrato>(payload).TipoCambioPago;
        return tc is > 0m ? tc : null;
    }

    private sealed record TipoCambioPagoContrato(decimal? TipoCambioPago);
}
