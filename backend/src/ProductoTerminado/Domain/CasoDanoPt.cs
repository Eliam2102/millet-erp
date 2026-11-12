using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.Domain;

/// <summary>
/// Caso de daño, rechazo o devolución de piezas (F1-PT-06, M-351, M-352; plano 1.7.1 §7.5). Las piezas quedan segregadas en revisión;
/// Calidad dictamina (Fernando, 11-nov) con evidencia y siendo otra persona distinta de quien registró: regresar a disponible, reproceso,
/// refabricación (pedido nuevo en A+W, el ERP no lo crea) o desecho. Si las piezas ya estaban facturadas, el caso liga la nota de crédito
/// por devolución o la solicitud de cancelación de la factura, y no se cierra mientras ese documento esté pendiente.
/// </summary>
public sealed class CasoDanoPt : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int MaxEvidenciaBytes = 2_000_000;
    private readonly List<CasoDanoPtPieza> _piezas = [];

    public Guid EmpresaId { get; set; }
    public string Folio { get; private set; } = "";
    public Guid SucursalId { get; private set; }
    public Guid PedidoPtId { get; private set; }
    public EtapaDanoPt Etapa { get; private set; }
    public string Motivo { get; private set; } = "";
    public byte[]? Evidencia { get; private set; }
    public Guid RegistradoPorId { get; private set; }
    public DateTimeOffset RegistradoAt { get; private set; }
    public EstadoCasoDanoPt Estado { get; private set; }
    public DictamenCalidadPt? Dictamen { get; private set; }
    public string? ObservacionDictamen { get; private set; }
    /// <summary>Refabricación: pedido nuevo que Ventas levantó en A+W (referencia, opcional al dictaminar).</summary>
    public string? PedidoRefabricacionAw { get; private set; }
    public Guid? DictaminadoPorId { get; private set; }
    public DateTimeOffset? DictaminadoAt { get; private set; }
    public CorrectivoFiscalPt? Correctivo { get; private set; }
    /// <summary>Nota de crédito emitida o factura cuya cancelación se pidió.</summary>
    public Guid? DocumentoFiscalId { get; private set; }
    public string? DocumentoFiscalFolio { get; private set; }
    public Guid? CerradoPorId { get; private set; }
    public DateTimeOffset? CerradoAt { get; private set; }
    public IReadOnlyCollection<CasoDanoPtPieza> Piezas => _piezas;

    private CasoDanoPt() { }
    private CasoDanoPt(Guid id) : base(id) { }

    public static CasoDanoPt Registrar(Guid empresa, Guid sucursalId, Guid pedidoPtId, EtapaDanoPt etapa, IReadOnlyCollection<PiezaPt> piezas, string motivo, byte[]? evidencia,
        Guid actor, DateTimeOffset ahora, DateOnly diaLocal)
    {
        if (piezas.Count == 0) throw new BusinessRuleException("PT_CASO_SIN_PIEZAS", "El caso lleva al menos una pieza.");
        if (piezas.Any(p => p.PedidoPtId != pedidoPtId)) throw new BusinessRuleException("PT_CASO_PIEZAS_OTRO_PEDIDO", "Las piezas del caso son de un mismo pedido.");
        var id = Guid.CreateVersion7();
        var caso = new CasoDanoPt(id)
        {
            EmpresaId = empresa, Folio = $"CD-{diaLocal:yyyyMMdd}-{id.ToString("N")[^5..].ToUpperInvariant()}", SucursalId = sucursalId, PedidoPtId = pedidoPtId,
            Etapa = etapa, Motivo = ReglasSalidaPt.MotivoValido(motivo), Evidencia = evidencia, RegistradoPorId = actor, RegistradoAt = ahora, Estado = EstadoCasoDanoPt.Abierto,
        };
        caso._piezas.AddRange(piezas.Select(p => new CasoDanoPtPieza(empresa, id, p.Id, p.Etiqueta, p.Posicion)));
        return caso;
    }

    /// <summary>Completa o reemplaza la evidencia antes del dictamen (la evidencia faltante se pide antes de dictaminar).</summary>
    public void AdjuntarEvidencia(byte[] evidencia)
    {
        if (Estado != EstadoCasoDanoPt.Abierto) throw new BusinessRuleException("PT_CASO_NO_ABIERTO", "La evidencia se completa antes del dictamen.");
        Evidencia = evidencia;
    }

    public void Dictaminar(DictamenCalidadPt dictamen, string observacion, string? pedidoRefabricacionAw, Guid actor, DateTimeOffset ahora)
    {
        if (Estado != EstadoCasoDanoPt.Abierto) throw new BusinessRuleException("PT_CASO_YA_DICTAMINADO", "El caso ya tiene dictamen.");
        if (Evidencia is null) throw new BusinessRuleException("PT_CASO_SIN_EVIDENCIA", "Falta la evidencia del daño: adjúntela antes de dictaminar.");
        if (actor == RegistradoPorId) throw new BusinessRuleException("PT_CASO_SEPARACION_FUNCIONES", "Quien registró el daño no lo dictamina.");
        var refab = string.IsNullOrWhiteSpace(pedidoRefabricacionAw) ? null : pedidoRefabricacionAw.Trim();
        if (refab is { Length: > 50 }) throw new BusinessRuleException("PT_CASO_PEDIDO_AW_INVALIDO", "El pedido de A+W tiene máximo 50 caracteres.");
        (Estado, Dictamen, ObservacionDictamen, PedidoRefabricacionAw, DictaminadoPorId, DictaminadoAt) =
            (EstadoCasoDanoPt.Dictaminado, dictamen, ReglasSalidaPt.MotivoValido(observacion), dictamen == DictamenCalidadPt.Refabricacion ? refab : null, actor, ahora);
    }

    /// <summary>M-352: liga la nota de crédito de devolución o la factura cuya cancelación se pidió. Uno por caso.</summary>
    public void LigarCorrectivo(CorrectivoFiscalPt tipo, Guid documentoId, string? folio)
    {
        ValidarCorrectivoPosible();
        (Correctivo, DocumentoFiscalId, DocumentoFiscalFolio) = (tipo, documentoId, folio);
    }

    /// <summary>Se llama antes de pedir el documento a Facturación: con el caso dictaminado y sin otro documento ligado.</summary>
    public void ValidarCorrectivoPosible()
    {
        if (Estado != EstadoCasoDanoPt.Dictaminado) throw new BusinessRuleException("PT_CASO_SIN_DICTAMEN", "El correctivo fiscal se resuelve con el caso ya dictaminado.");
        if (Correctivo is not null) throw new BusinessRuleException("PT_CASO_CON_CORRECTIVO", "El caso ya tiene su documento fiscal ligado.");
    }

    /// <summary>Cierra con el dictamen aplicado. <paramref name="correctivoPendiente"/>: la nota sin timbrar o la factura aún sin cancelar.</summary>
    public void Cerrar(bool correctivoPendiente, Guid actor, DateTimeOffset ahora)
    {
        if (Estado != EstadoCasoDanoPt.Dictaminado) throw new BusinessRuleException("PT_CASO_SIN_DICTAMEN", "El caso se cierra después del dictamen de Calidad.");
        if (correctivoPendiente) throw new BusinessRuleException("PT_CASO_CORRECTIVO_PENDIENTE", "El documento fiscal del caso sigue pendiente (nota sin timbrar o cancelación sin concluir).");
        (Estado, CerradoPorId, CerradoAt) = (EstadoCasoDanoPt.Cerrado, actor, ahora);
    }
}

public sealed class CasoDanoPtPieza : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid CasoDanoPtId { get; private set; }
    public Guid AggregateRootId => CasoDanoPtId;
    public Guid PiezaId { get; private set; }
    public string Etiqueta { get; private set; } = "";
    public int Posicion { get; private set; }

    private CasoDanoPtPieza() { }

    internal CasoDanoPtPieza(Guid empresa, Guid casoId, Guid piezaId, string etiqueta, int posicion) : base(Guid.CreateVersion7())
        => (EmpresaId, CasoDanoPtId, PiezaId, Etiqueta, Posicion) = (empresa, casoId, piezaId, etiqueta, posicion);
}
