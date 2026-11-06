using System.Globalization;
using System.Text;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.Domain;

/// <summary>ConDiferencia: falta albarán firmado o el cobro no coincide con el pedido; Cerrada: todo cuadra y ya no se cambia.</summary>
public enum EstadoLiquidacion { ConDiferencia, Cerrada }

/// <summary>
/// Liquidación del chofer en caja (PT5.7), una por hoja de salida emitida: por albarán, si volvió firmado y lo cobrado (efectivo y voucher)
/// contra lo que debía cobrar; más los imprevistos de la ruta. Con diferencia no se cierra: se corrige y se vuelve a liquidar.
/// </summary>
public sealed class LiquidacionRutaPt : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    private readonly List<LiquidacionAlbaranPt> _albaranes = [];
    private readonly List<ImprevistoRutaPt> _imprevistos = [];

    public Guid EmpresaId { get; set; }
    public Guid SalidaPtId { get; private set; }
    public EstadoLiquidacion Estado { get; private set; }
    public DateTimeOffset LiquidadaAt { get; private set; }
    public string Usuario { get; private set; } = "";
    public IReadOnlyCollection<LiquidacionAlbaranPt> Albaranes => _albaranes;
    public IReadOnlyCollection<ImprevistoRutaPt> Imprevistos => _imprevistos;

    private LiquidacionRutaPt() { }

    public LiquidacionRutaPt(Guid empresa, Guid salidaId) : base(Guid.CreateVersion7()) => (EmpresaId, SalidaPtId) = (empresa, salidaId);

    /// <summary>Una condición «Crédito 24 horas» es contra entrega (Manual §2.3); el resto no se cobra en la ruta.</summary>
    public static bool EsContraEntrega(string? condicionPago)
    {
        if (string.IsNullOrWhiteSpace(condicionPago)) return false;
        var s = new string(condicionPago.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant();
        return s.Contains("CREDITO") && s.Contains("24");
    }

    /// <summary>Guarda lo capturado en caja (reemplaza lo anterior) y decide si cierra. Una liquidación cerrada no se toca.</summary>
    public void Registrar(IEnumerable<(AlbaranPt Albaran, decimal Esperado, bool Firmado, decimal Efectivo, decimal Voucher, string? Referencia)> lineas, IEnumerable<(string Descripcion, decimal Importe)> imprevistos, DateTimeOffset ahora, string usuario)
    {
        if (Estado == EstadoLiquidacion.Cerrada)
            throw new BusinessRuleException("PT_LIQUIDACION_CERRADA", "La liquidación ya está cerrada.");
        foreach (var l in lineas)
        {
            if (l.Efectivo < 0 || l.Voucher < 0) throw new BusinessRuleException("PT_LIQUIDACION_IMPORTE_NEGATIVO", "Los importes cobrados no pueden ser negativos.");
            if (l.Voucher > 0 && string.IsNullOrWhiteSpace(l.Referencia))
                throw new BusinessRuleException("PT_LIQUIDACION_VOUCHER_SIN_REFERENCIA", "El voucher lleva su folio para conciliarlo con el pedido.");
            var existente = _albaranes.FirstOrDefault(a => a.AlbaranPtId == l.Albaran.Id);
            if (existente is null) _albaranes.Add(new LiquidacionAlbaranPt(EmpresaId, Id, l.Albaran.Id, l.Esperado, l.Firmado, l.Efectivo, l.Voucher, l.Referencia));
            else existente.Actualizar(l.Esperado, l.Firmado, l.Efectivo, l.Voucher, l.Referencia);
        }
        _imprevistos.Clear();
        _imprevistos.AddRange(imprevistos.Select(i => new ImprevistoRutaPt(EmpresaId, Id, i.Descripcion, i.Importe)));
        (Estado, LiquidadaAt, Usuario) = (_albaranes.All(a => a.Conciliado) ? EstadoLiquidacion.Cerrada : EstadoLiquidacion.ConDiferencia, ahora, usuario);
    }
}

/// <summary>Lo que el chofer entregó de un albarán. <c>Diferencia</c> = lo que debía cobrar menos lo recibido (positiva = falta).</summary>
public sealed class LiquidacionAlbaranPt : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid LiquidacionRutaPtId { get; private set; }
    public Guid AggregateRootId => LiquidacionRutaPtId;
    public Guid AlbaranPtId { get; private set; }
    public decimal Esperado { get; private set; }
    public bool Firmado { get; private set; }
    public decimal Efectivo { get; private set; }
    public decimal Voucher { get; private set; }
    public string? VoucherReferencia { get; private set; }
    public decimal Diferencia => Esperado - Efectivo - Voucher;
    public bool Conciliado => Firmado && Diferencia == 0m;

    private LiquidacionAlbaranPt() { }

    internal LiquidacionAlbaranPt(Guid empresa, Guid liquidacionId, Guid albaranId, decimal esperado, bool firmado, decimal efectivo, decimal voucher, string? referencia) : base(Guid.CreateVersion7())
    {
        (EmpresaId, LiquidacionRutaPtId, AlbaranPtId) = (empresa, liquidacionId, albaranId);
        Actualizar(esperado, firmado, efectivo, voucher, referencia);
    }

    internal void Actualizar(decimal esperado, bool firmado, decimal efectivo, decimal voucher, string? referencia)
        => (Esperado, Firmado, Efectivo, Voucher, VoucherReferencia) = (esperado, firmado, efectivo, voucher, voucher > 0 ? referencia?.Trim() : null);
}

/// <summary>Imprevisto de la ruta (descarga que se demoró, maniobra extra, desvío): queda registrado con su importe, no entra a la diferencia de cobro.</summary>
public sealed class ImprevistoRutaPt : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid LiquidacionRutaPtId { get; private set; }
    public Guid AggregateRootId => LiquidacionRutaPtId;
    public string Descripcion { get; private set; } = "";
    public decimal Importe { get; private set; }

    private ImprevistoRutaPt() { }

    internal ImprevistoRutaPt(Guid empresa, Guid liquidacionId, string descripcion, decimal importe) : base(Guid.CreateVersion7())
        => (EmpresaId, LiquidacionRutaPtId, Descripcion, Importe) = (empresa, liquidacionId, descripcion.Trim(), importe);
}

/// <summary>Aviso a CxC de un albarán a contra entrega que no se liquidó el día de la salida. Uno por albarán: avisar de nuevo no repite.</summary>
public sealed class AvisoContraEntregaPt : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid AlbaranPtId { get; private set; }
    public Guid PedidoPtId { get; private set; }
    public decimal Importe { get; private set; }
    public DateTimeOffset AvisadoAt { get; private set; }

    private AvisoContraEntregaPt() { }

    public AvisoContraEntregaPt(Guid empresa, Guid albaranId, Guid pedidoId, decimal importe, DateTimeOffset ahora) : base(Guid.CreateVersion7())
        => (EmpresaId, AlbaranPtId, PedidoPtId, Importe, AvisadoAt) = (empresa, albaranId, pedidoId, importe, ahora);
}
