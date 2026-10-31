using System.Text.Json.Serialization;
using Millet.Contabilidad.Application.Polizas;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

/// <summary>Lectura del saldo de la puente por OC: deudor = se facturó más de lo recibido; acreedor = se recibió más de lo facturado.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NaturalezaSaldoPuente : short { Saldada = 0, FacturadoSinRecibir = 1, RecibidoSinFacturar = 2 }

/// <summary>Tramo de antigüedad del saldo de la OC (K10.5-c): 0–30, 31–60 y más de 60 días.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TramoAntiguedadPuente : short { De0a30 = 1, De31a60 = 2, MasDe60 = 3 }

/// <summary>
/// Movimiento de la cuenta puente recepción–factura (K10.5) ligado a la orden de compra que lo explica. Es el «auxiliar por OC» de la
/// puente: una fila por partida de póliza automática que afectó la puente con referencia de OC. La recepción abona, la factura carga;
/// la OC queda saldada cuando ambas coinciden. Importes en MXN (los de la partida), siempre positivos en su lado; la anulación de la
/// póliza se refleja con una reversa (lado contrario) ligada al movimiento original, nunca borrando.
/// </summary>
public sealed class MovimientoPuente : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int FolioMaximo = 60;
    /// <summary>Por debajo de un centavo la OC se considera saldada.</summary>
    public const decimal Tolerancia = 0.01m;

    public Guid EmpresaId { get; set; }
    public Guid PolizaId { get; private set; }
    /// <summary>Partida de la póliza que originó el movimiento. En la reversa es la partida original (la reversa no tiene partida propia).</summary>
    public Guid PolizaPartidaId { get; private set; }
    public string? PolizaFolio { get; private set; }
    public DateOnly Fecha { get; private set; }
    public Guid CuentaId { get; private set; }
    public Guid SucursalId { get; private set; }
    public Guid? ProveedorId { get; private set; }
    public Guid OrdenCompraId { get; private set; }
    public string? OrdenCompraFolio { get; private set; }
    public Guid? LineaOcId { get; private set; }
    /// <summary>Documento que movió la puente, con los valores del contrato de C1.4 (<see cref="ReferenciaPuente"/>).</summary>
    public TipoDocumentoPuente TipoDocumento { get; private set; }
    public Guid DocumentoId { get; private set; }
    /// <summary>Factura a la que se aplica una nota de cargo o de crédito (con ella se resuelve la OC si el módulo no la conoce).</summary>
    public Guid? FacturaOrigenId { get; private set; }
    public decimal Cargo { get; private set; }
    public decimal Abono { get; private set; }
    public string Moneda { get; private set; } = Poliza.MonedaNacional;
    public decimal TipoCambio { get; private set; } = 1m;
    public decimal? ImporteOriginal { get; private set; }
    public bool EsReversa { get; private set; }
    public Guid? MovimientoRevertidoId { get; private set; }

    private MovimientoPuente() { }

    private MovimientoPuente(Guid id) : base(id) { }

    /// <summary>Movimiento de una partida que afectó la puente con referencia de OC. Un solo lado, positivo.</summary>
    public static MovimientoPuente Registrar(Guid id, Guid empresaId, Guid polizaId, Guid polizaPartidaId, string? polizaFolio, DateOnly fecha,
        Guid cuentaId, Guid sucursalId, Guid? proveedorId, Guid ordenCompraId, string? ordenCompraFolio, Guid? lineaOcId,
        TipoDocumentoPuente tipoDocumento, Guid documentoId, Guid? facturaOrigenId, decimal cargo, decimal abono, string moneda,
        decimal tipoCambio, decimal? importeOriginal)
    {
        if (ordenCompraId == Guid.Empty)
            throw new BusinessRuleException("PUENTE_OC_NO_RESUELTA", "El movimiento de la cuenta puente requiere la orden de compra.");
        if (documentoId == Guid.Empty || polizaId == Guid.Empty || polizaPartidaId == Guid.Empty || cuentaId == Guid.Empty || sucursalId == Guid.Empty)
            throw new BusinessRuleException("PUENTE_REFERENCIA_INCOMPLETA", "El movimiento de la cuenta puente requiere póliza, partida, cuenta, sucursal y documento.");
        if (!Enum.IsDefined(tipoDocumento))
            throw new BusinessRuleException("PUENTE_TIPO_DOCUMENTO_INVALIDO", "Tipo de documento de la cuenta puente no reconocido.");
        if (cargo < 0m || abono < 0m || (cargo == 0m) == (abono == 0m) || decimal.Round(cargo, 2) != cargo || decimal.Round(abono, 2) != abono)
            throw new BusinessRuleException("PUENTE_IMPORTE_INVALIDO", "El movimiento de la cuenta puente lleva un cargo o un abono positivo (solo uno, hasta dos decimales).");
        var mon = (moneda ?? Poliza.MonedaNacional).Trim().ToUpperInvariant();
        if (mon.Length != 3 || !mon.All(char.IsAsciiLetterUpper) || tipoCambio <= 0m)
            throw new BusinessRuleException("PUENTE_MONEDA_INVALIDA", "La moneda es un código ISO de tres letras y el tipo de cambio es positivo.");
        if (ordenCompraFolio?.Trim().Length > FolioMaximo || polizaFolio?.Trim().Length > FolioMaximo)
            throw new BusinessRuleException("PUENTE_FOLIO_INVALIDO", $"Los folios admiten hasta {FolioMaximo} caracteres.");

        return new MovimientoPuente(id)
        {
            EmpresaId = empresaId,
            PolizaId = polizaId,
            PolizaPartidaId = polizaPartidaId,
            PolizaFolio = Limpio(polizaFolio),
            Fecha = fecha,
            CuentaId = cuentaId,
            SucursalId = sucursalId,
            ProveedorId = proveedorId,
            OrdenCompraId = ordenCompraId,
            OrdenCompraFolio = Limpio(ordenCompraFolio),
            LineaOcId = lineaOcId,
            TipoDocumento = tipoDocumento,
            DocumentoId = documentoId,
            FacturaOrigenId = facturaOrigenId,
            Cargo = cargo,
            Abono = abono,
            Moneda = mon,
            TipoCambio = mon == Poliza.MonedaNacional ? 1m : tipoCambio,
            ImporteOriginal = mon == Poliza.MonedaNacional ? null : importeOriginal is { } o ? Math.Abs(o) : null,
        };
    }

    /// <summary>
    /// Reversa por anulación de la póliza (K10.3 → K10.5-e): mismo documento y OC, lado contrario, fecha y póliza de la anulación. Así
    /// la OC regresa al saldo que tenía antes del documento anulado.
    /// </summary>
    public MovimientoPuente Revertir(Guid id, Guid polizaAnulacionId, string? folio, DateOnly fecha)
    {
        if (EsReversa)
            throw new BusinessRuleException("PUENTE_REVERSA_NO_REVERSIBLE", "Una reversa de la cuenta puente no se revierte.");
        var r = Registrar(id, EmpresaId, polizaAnulacionId, PolizaPartidaId, folio, fecha, CuentaId, SucursalId, ProveedorId, OrdenCompraId,
            OrdenCompraFolio, LineaOcId, TipoDocumento, DocumentoId, FacturaOrigenId, Abono, Cargo, Moneda, TipoCambio, ImporteOriginal);
        r.EsReversa = true;
        r.MovimientoRevertidoId = Id;
        return r;
    }

    private static string? Limpio(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

/// <summary>Saldo de la puente de una OC al corte, con su naturaleza, antigüedad y diferencia cambiaria (informativa, sin asentar).</summary>
public sealed record SaldoPuenteOc(
    decimal Cargos, decimal Abonos, decimal Saldo, NaturalezaSaldoPuente Naturaleza, DateOnly? FechaPrimerMovimientoSinSaldar,
    int? DiasAntiguedad, TramoAntiguedadPuente? Tramo, decimal? DiferenciaCambiaria);

/// <summary>Cálculo puro de la conciliación de la cuenta puente por OC (K10.5).</summary>
public static class ConciliacionPuente
{
    public static string Etiqueta(NaturalezaSaldoPuente n) => n switch
    {
        NaturalezaSaldoPuente.FacturadoSinRecibir => "Facturado sin recibir",
        NaturalezaSaldoPuente.RecibidoSinFacturar => "Recibido sin facturar",
        _ => "Saldada",
    };

    public static string Etiqueta(TramoAntiguedadPuente t) => t switch
    {
        TramoAntiguedadPuente.De0a30 => "0–30",
        TramoAntiguedadPuente.De31a60 => "31–60",
        _ => "Más de 60",
    };

    public static TramoAntiguedadPuente Tramo(int dias) => dias switch
    {
        <= 30 => TramoAntiguedadPuente.De0a30,
        <= 60 => TramoAntiguedadPuente.De31a60,
        _ => TramoAntiguedadPuente.MasDe60,
    };

    /// <summary>
    /// Saldo (cargos − abonos) de los movimientos de UNA OC con fecha hasta el corte. La antigüedad corre desde el primer movimiento sin
    /// saldar: el primero después de la última vez que el saldo acumulado quedó en cero (si nunca quedó en cero, el primero).
    /// La diferencia cambiaria aplica cuando todos los movimientos están en la misma moneda extranjera y la OC quedó saldada en esa
    /// moneda: el saldo en pesos que queda es atribuible al tipo de cambio (se informa, no se asienta).
    /// </summary>
    public static SaldoPuenteOc Calcular(IEnumerable<MovimientoPuente> movimientos, DateOnly corte)
    {
        var movs = movimientos.Where(m => m.Fecha <= corte).OrderBy(m => m.Fecha).ThenBy(m => m.EsReversa).ThenBy(m => m.Id).ToList();
        decimal acumulado = 0m;
        DateOnly? inicio = null;
        foreach (var m in movs)
        {
            inicio ??= m.Fecha;
            acumulado += m.Cargo - m.Abono;
            if (Math.Abs(acumulado) < MovimientoPuente.Tolerancia) inicio = null;
        }
        var cargos = movs.Sum(m => m.Cargo);
        var abonos = movs.Sum(m => m.Abono);
        var saldo = cargos - abonos;
        var naturaleza = Math.Abs(saldo) < MovimientoPuente.Tolerancia ? NaturalezaSaldoPuente.Saldada
            : saldo > 0m ? NaturalezaSaldoPuente.FacturadoSinRecibir : NaturalezaSaldoPuente.RecibidoSinFacturar;
        if (naturaleza == NaturalezaSaldoPuente.Saldada) inicio = null;
        int? dias = inicio is { } f ? corte.DayNumber - f.DayNumber : null;
        return new SaldoPuenteOc(cargos, abonos, saldo, naturaleza, inicio, dias, dias is { } d ? Tramo(d) : null, DiferenciaCambiaria(movs, saldo));
    }

    private static decimal? DiferenciaCambiaria(List<MovimientoPuente> movs, decimal saldo)
    {
        if (movs.Count == 0) return null;
        var moneda = movs[0].Moneda;
        if (moneda == Poliza.MonedaNacional || movs.Any(m => m.Moneda != moneda || m.ImporteOriginal is null)) return null;
        var original = movs.Sum(m => m.Cargo > 0m ? m.ImporteOriginal!.Value : -m.ImporteOriginal!.Value);
        return Math.Abs(original) < MovimientoPuente.Tolerancia ? saldo : null;
    }
}
