using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.Domain;

/// <summary>
/// Confirmación de entrega de un albarán (PT5.6): quién recibió, cuándo, con firma o foto como evidencia, lo rechazado en sitio y,
/// si el chofer cobró (contra entrega), el voucher que luego se concilia al liquidar en caja. Una por albarán.
/// </summary>
public sealed class EntregaPt : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int MaxFirmaBytes = 300_000;
    public const int MaxFotoBytes = 2_000_000;

    public Guid EmpresaId { get; set; }
    public Guid SalidaPtId { get; private set; }
    public Guid AlbaranPtId { get; private set; }
    public Guid PedidoPtId { get; private set; }
    public ResultadoEntrega Resultado { get; private set; }
    public string Receptor { get; private set; } = "";
    public int Entregadas { get; private set; }
    public int Rechazadas { get; private set; }
    public string? MotivoRechazo { get; private set; }
    public byte[]? Firma { get; private set; }
    public byte[]? Foto { get; private set; }
    public decimal? CobroMonto { get; private set; }
    public string? CobroForma { get; private set; }
    public string? CobroReferencia { get; private set; }
    /// <summary>Se llena cuando la caja liquida la ruta y el cobro coincide con el voucher (CA5.6).</summary>
    public DateTimeOffset? CobroConciliadoAt { get; private set; }
    public Guid? CobroMostradorId { get; private set; }
    public DateTimeOffset EntregadaAt { get; private set; }
    public string Usuario { get; private set; } = "";

    private EntregaPt() { }

    public static EntregaPt Crear(Guid empresa, Guid salidaId, AlbaranPt albaran, int entregadas, int rechazadas, string? motivo, string receptor, byte[]? firma, byte[]? foto,
        decimal? cobroMonto, string? cobroForma, string? cobroReferencia, DateTimeOffset ahora, string usuario)
    {
        if (firma is null && foto is null)
            throw new BusinessRuleException("PT_ENTREGA_SIN_EVIDENCIA", "La entrega necesita la firma de quien recibe o una foto como evidencia.");
        if (string.IsNullOrWhiteSpace(receptor))
            throw new BusinessRuleException("PT_ENTREGA_SIN_RECEPTOR", "Indica quién recibe.");
        if (rechazadas > 0 && string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("PT_ENTREGA_SIN_MOTIVO", "El rechazo en sitio lleva el motivo.");
        return new EntregaPt(Guid.CreateVersion7())
        {
            EmpresaId = empresa, SalidaPtId = salidaId, AlbaranPtId = albaran.Id, PedidoPtId = albaran.PedidoPtId,
            Resultado = entregadas == 0 ? ResultadoEntrega.Rechazada : rechazadas == 0 ? ResultadoEntrega.Entregada : ResultadoEntrega.Parcial,
            Receptor = receptor.Trim(), Entregadas = entregadas, Rechazadas = rechazadas, MotivoRechazo = rechazadas > 0 ? motivo!.Trim() : null,
            Firma = firma, Foto = foto, CobroMonto = cobroMonto, CobroForma = cobroForma?.Trim(), CobroReferencia = cobroReferencia?.Trim(), EntregadaAt = ahora, Usuario = usuario,
        };
    }

    private EntregaPt(Guid id) : base(id) { }

    /// <summary>Correcciones aplicadas (M-248); la versión vigente es <c>Correcciones + 1</c>.</summary>
    public int Correcciones { get; private set; }

    /// <summary>
    /// M-248: corrige lo entregado y rechazado con motivo y evidencia nueva, antes de cerrar la liquidación de la ruta. Devuelve la versión
    /// anterior completa (con su evidencia) para guardarla: la historia no se pierde.
    /// </summary>
    public EntregaPtVersion Corregir(int entregadas, int rechazadas, string? motivoRechazo, string receptor, byte[]? firma, byte[]? foto, string motivoCorreccion,
        DateTimeOffset ahora, string usuario)
    {
        if (firma is null && foto is null)
            throw new BusinessRuleException("PT_ENTREGA_SIN_EVIDENCIA", "La corrección necesita evidencia nueva: firma o foto del albarán.");
        if (string.IsNullOrWhiteSpace(receptor)) throw new BusinessRuleException("PT_ENTREGA_SIN_RECEPTOR", "Indica quién recibe.");
        if (rechazadas > 0 && string.IsNullOrWhiteSpace(motivoRechazo))
            throw new BusinessRuleException("PT_ENTREGA_SIN_MOTIVO", "El rechazo en sitio lleva el motivo.");
        var motivo = ReglasSalidaPt.MotivoValido(motivoCorreccion);
        var anterior = new EntregaPtVersion(EmpresaId, Id, Correcciones + 1, Resultado, Receptor, Entregadas, Rechazadas, MotivoRechazo, Firma, Foto, EntregadaAt, Usuario, motivo, ahora, usuario);
        Correcciones++;
        (Entregadas, Rechazadas, MotivoRechazo, Receptor, Firma, Foto) = (entregadas, rechazadas, rechazadas > 0 ? motivoRechazo!.Trim() : null, receptor.Trim(), firma, foto);
        Resultado = entregadas == 0 ? ResultadoEntrega.Rechazada : rechazadas == 0 ? ResultadoEntrega.Entregada : ResultadoEntrega.Parcial;
        return anterior;
    }

    /// <summary>Devuelve false si ya estaba conciliado o no hay cobro que conciliar.</summary>
    public bool ConciliarCobro(Guid cobroMostradorId, DateTimeOffset ahora)
    {
        if (CobroMonto is null || CobroConciliadoAt is not null) return false;
        (CobroMostradorId, CobroConciliadoAt) = (cobroMostradorId, ahora);
        return true;
    }
}

/// <summary>M-248: versión anterior de una entrega corregida (Numero 1 = la confirmación original). <c>MotivoCorreccion</c> es por qué se reemplazó.</summary>
public sealed class EntregaPtVersion : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid EntregaPtId { get; private set; }
    public int Numero { get; private set; }
    public ResultadoEntrega Resultado { get; private set; }
    public string Receptor { get; private set; } = "";
    public int Entregadas { get; private set; }
    public int Rechazadas { get; private set; }
    public string? MotivoRechazo { get; private set; }
    public byte[]? Firma { get; private set; }
    public byte[]? Foto { get; private set; }
    public DateTimeOffset EntregadaAt { get; private set; }
    public string Usuario { get; private set; } = "";
    public string MotivoCorreccion { get; private set; } = "";
    public DateTimeOffset CorregidaAt { get; private set; }
    public string CorregidaPor { get; private set; } = "";

    private EntregaPtVersion() { }

    internal EntregaPtVersion(Guid empresa, Guid entregaId, int numero, ResultadoEntrega resultado, string receptor, int entregadas, int rechazadas, string? motivoRechazo,
        byte[]? firma, byte[]? foto, DateTimeOffset entregadaAt, string usuario, string motivoCorreccion, DateTimeOffset corregidaAt, string corregidaPor) : base(Guid.CreateVersion7())
    {
        (EmpresaId, EntregaPtId, Numero, Resultado, Receptor, Entregadas, Rechazadas, MotivoRechazo) = (empresa, entregaId, numero, resultado, receptor, entregadas, rechazadas, motivoRechazo);
        (Firma, Foto, EntregadaAt, Usuario, MotivoCorreccion, CorregidaAt, CorregidaPor) = (firma, foto, entregadaAt, usuario, motivoCorreccion, corregidaAt, corregidaPor);
    }
}
