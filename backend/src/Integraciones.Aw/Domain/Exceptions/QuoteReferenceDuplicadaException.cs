using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Domain.Exceptions;

/// <summary>
/// Se lanza cuando se intenta registrar una entidad
/// (<see cref="TipoEntidad.Cotizacion"/> u otra) con una
/// <c>referencia_externa</c> que ya existe para la misma empresa.
///
/// <para>
/// Type Problem Details: <c>https://millet-erp/errors/aw_quote_reference_duplicada</c>.
/// HTTP 409 Conflict (heredado del mapping de
/// <see cref="ConflictException"/> en <c>GlobalExceptionHandler</c> — PR D).
/// La unicidad la garantiza el UNIQUE constraint
/// <c>uq_tipo_referencia (tipo_entidad, referencia_externa, empresa_id)</c>;
/// el handler atrapa <c>DbUpdateException</c> de Postgres SqlState 23505
/// y lanza esta excepción para que llegue serializada al cliente.
/// </para>
/// </summary>
public sealed class QuoteReferenceDuplicadaException : ConflictException
{
    public const string CodeValue = "AW_QUOTE_REFERENCE_DUPLICADA";

    public TipoEntidad TipoEntidad { get; }
    public string ReferenciaExterna { get; }
    public Guid EmpresaId { get; }

    public QuoteReferenceDuplicadaException(
        TipoEntidad tipoEntidad,
        string referenciaExterna,
        Guid empresaId)
        : base(
            CodeValue,
            $"Ya existe una entidad de tipo '{tipoEntidad}' con referencia '{referenciaExterna}' " +
            $"en la empresa {empresaId}. Las referencias externas son únicas por (tipo, empresa).")
    {
        TipoEntidad = tipoEntidad;
        ReferenciaExterna = referenciaExterna;
        EmpresaId = empresaId;
    }
}
