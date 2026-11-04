using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.Conciliacion;

/// <summary>Quién disparó la corrida de conciliación (C7.7).</summary>
public enum OrigenConciliacion : short
{
    /// <summary>Worker diario: concilia el día anterior.</summary>
    Programada = 1,

    /// <summary>Un usuario de CxC la pidió para una fecha.</summary>
    Demanda = 2,
}

/// <summary>Qué no cuadra entre Facturación y la cartera (C7.7).</summary>
public enum TipoDiferenciaConciliacion : short
{
    /// <summary>Factura timbrada que no está en cartera (C7.7-a).</summary>
    FacturaNoEnCartera = 1,

    /// <summary>NC timbrada sin su movimiento en cartera (C7.7-c).</summary>
    NotaCreditoNoReflejada = 2,

    /// <summary>REP o cobro de mostrador sin su movimiento de pago en cartera.</summary>
    PagoNoReflejado = 3,

    /// <summary>Cancelado en Facturación y todavía vigente en cartera.</summary>
    CancelacionPendiente = 4,

    /// <summary>Está en los dos lados con distinto importe.</summary>
    ImporteDistinto = 5,

    /// <summary>Cancelado en cartera y vigente en Facturación.</summary>
    CanceladoSoloEnCartera = 6,
}

/// <summary>
/// Corrida de la conciliación diaria de cartera contra Facturación (C7.7, R19).
/// Cada corrida queda registrada, también la que no encuentra diferencias
/// (C7.7-b); una corrida nueva no sobrescribe a las anteriores.
/// </summary>
public sealed class ConciliacionCartera : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    /// <summary>Día contable conciliado (hora de Millet, mismo criterio que Facturación).</summary>
    public DateOnly Fecha { get; private set; }

    public OrigenConciliacion Origen { get; private set; }
    public Guid? EjecutadaPor { get; private set; }
    public DateTimeOffset EjecutadaEn { get; private set; }

    /// <summary>Comprobantes de Facturación revisados (facturas, NC, REP, cobros y cancelaciones).</summary>
    public int ComprobantesRevisados { get; private set; }

    public int TotalDiferencias { get; private set; }

    private readonly List<DiferenciaConciliacion> _diferencias = [];
    public IReadOnlyCollection<DiferenciaConciliacion> Diferencias => _diferencias.AsReadOnly();

    private ConciliacionCartera() { }

    public static ConciliacionCartera Registrar(
        Guid empresaId,
        DateOnly fecha,
        OrigenConciliacion origen,
        Guid? ejecutadaPor,
        DateTimeOffset ejecutadaEn,
        int comprobantesRevisados,
        IEnumerable<DiferenciaCalculada> diferencias)
    {
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("CC_EMPRESA_VACIA", "La empresa es obligatoria.");
        if (comprobantesRevisados < 0)
            throw new BusinessRuleException("CC_REVISADOS_INVALIDO", "Los comprobantes revisados no pueden ser negativos.");

        var corrida = new ConciliacionCartera
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            Fecha = fecha,
            Origen = origen,
            EjecutadaPor = ejecutadaPor,
            EjecutadaEn = ejecutadaEn,
            ComprobantesRevisados = comprobantesRevisados,
        };
        foreach (var d in diferencias)
            corrida._diferencias.Add(new DiferenciaConciliacion(empresaId, corrida.Id, d));
        corrida.TotalDiferencias = corrida._diferencias.Count;
        return corrida;
    }
}

/// <summary>
/// Una diferencia encontrada en una corrida, con su causa probable. Marcada
/// <see cref="INotAudited"/>: es un renglón inmutable que nace con su corrida, y la
/// corrida ya es auditable (ADR-0008, Capa 4).
/// </summary>
public sealed class DiferenciaConciliacion : BaseEntity, IPerteneceAEmpresa, INotAudited
{
    public Guid EmpresaId { get; set; }
    public Guid ConciliacionId { get; private set; }

    public TipoDiferenciaConciliacion Tipo { get; private set; }

    /// <summary>Comprobante de Facturación que la origina (factura, NC, REP o cobro).</summary>
    public Guid ComprobanteId { get; private set; }
    public string TipoComprobante { get; private set; } = default!;
    public string Folio { get; private set; } = default!;
    public string? Uuid { get; private set; }

    /// <summary>Factura afectada en cartera (la misma factura cuando el comprobante es la factura).</summary>
    public Guid FacturaVentaId { get; private set; }
    public string ReceptorRfc { get; private set; } = default!;
    public string ReceptorNombre { get; private set; } = default!;

    public decimal ImporteFacturacion { get; private set; }
    public decimal? ImporteCartera { get; private set; }
    public string CausaProbable { get; private set; } = default!;

    private DiferenciaConciliacion() { }

    internal DiferenciaConciliacion(Guid empresaId, Guid conciliacionId, DiferenciaCalculada d) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresaId;
        ConciliacionId = conciliacionId;
        Tipo = d.Tipo;
        ComprobanteId = d.ComprobanteId;
        TipoComprobante = d.TipoComprobante;
        Folio = d.Folio;
        Uuid = d.Uuid;
        FacturaVentaId = d.FacturaVentaId;
        ReceptorRfc = d.ReceptorRfc;
        ReceptorNombre = d.ReceptorNombre;
        ImporteFacturacion = d.ImporteFacturacion;
        ImporteCartera = d.ImporteCartera;
        CausaProbable = d.CausaProbable;
    }
}

/// <summary>Resultado del comparador antes de persistirse.</summary>
public sealed record DiferenciaCalculada(
    TipoDiferenciaConciliacion Tipo,
    Guid ComprobanteId,
    string TipoComprobante,
    string Folio,
    string? Uuid,
    Guid FacturaVentaId,
    string ReceptorRfc,
    string ReceptorNombre,
    decimal ImporteFacturacion,
    decimal? ImporteCartera,
    string CausaProbable);
