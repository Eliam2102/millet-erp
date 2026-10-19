using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Revision;

/// <summary>
/// Dictamen auditable sobre una línea de material en revisión (ALM-04, G1.3).
/// Tabla <c>almacen.dictamenes_revision</c>. Inmutable: un dictamen no se deshace.
/// </summary>
public sealed class DictamenRevision : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid LineaRevisionId { get; private set; }
    public Guid MovimientoRevisionId { get; private set; }
    public ResultadoDictamen Resultado { get; private set; }
    public decimal Cantidad { get; private set; }
    public string Motivo { get; private set; } = string.Empty;
    public Guid DictaminadoPor { get; private set; }
    public DateTimeOffset DictaminadoAt { get; private set; }
    public Guid? MovimientoSalidaRevisionId { get; private set; }
    public Guid? MovimientoEntradaId { get; private set; }
    public Guid? DevolucionAProveedorId { get; private set; }

    private DictamenRevision() { }

    public DictamenRevision(
        Guid id,
        Guid empresaId,
        Guid lineaRevisionId,
        Guid movimientoRevisionId,
        ResultadoDictamen resultado,
        decimal cantidad,
        string motivo,
        Guid dictaminadoPor,
        Guid? movimientoSalidaRevisionId = null,
        Guid? movimientoEntradaId = null,
        Guid? devolucionAProveedorId = null) : base(id)
    {
        if (cantidad <= 0)
            throw new BusinessRuleException("DICTAMEN_CANTIDAD_INVALIDA",
                "La cantidad dictaminada debe ser mayor a cero.");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500)
            throw new BusinessRuleException("DICTAMEN_MOTIVO_INVALIDO",
                "El motivo del dictamen es requerido (máximo 500 caracteres).");
        if (dictaminadoPor == Guid.Empty)
            throw new BusinessRuleException("DICTAMEN_SIN_USUARIO", "Se requiere usuario autenticado.");

        var vinculosOk = resultado switch
        {
            ResultadoDictamen.Liberar => movimientoSalidaRevisionId.HasValue && movimientoEntradaId.HasValue
                && !devolucionAProveedorId.HasValue,
            ResultadoDictamen.Baja => movimientoSalidaRevisionId.HasValue && !movimientoEntradaId.HasValue
                && !devolucionAProveedorId.HasValue,
            ResultadoDictamen.Rechazar => devolucionAProveedorId.HasValue && !movimientoSalidaRevisionId.HasValue
                && !movimientoEntradaId.HasValue,
            _ => false,
        };
        if (!vinculosOk)
            throw new BusinessRuleException("DICTAMEN_VINCULO_INVALIDO",
                "El dictamen no tiene los movimientos o la devolución que corresponden a su resultado.");

        EmpresaId = empresaId;
        LineaRevisionId = lineaRevisionId;
        MovimientoRevisionId = movimientoRevisionId;
        Resultado = resultado;
        Cantidad = cantidad;
        Motivo = motivo;
        DictaminadoPor = dictaminadoPor;
        DictaminadoAt = DateTimeOffset.UtcNow;
        MovimientoSalidaRevisionId = movimientoSalidaRevisionId;
        MovimientoEntradaId = movimientoEntradaId;
        DevolucionAProveedorId = devolucionAProveedorId;
    }
}
