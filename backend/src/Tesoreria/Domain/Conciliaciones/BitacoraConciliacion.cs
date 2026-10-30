using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Conciliaciones;

/// <summary>
/// Bitácora inmutable de eventos de conciliación: desemparejamientos (T9.2-a / CA9.5),
/// reaperturas, y cambios con motivo y auditoría.
/// </summary>
public sealed class BitacoraConciliacion : BaseEntity
{
    public Guid ConciliacionId { get; private set; }
    public Guid? ExtractoLineaId { get; private set; }
    public Guid? MovimientoBancarioId { get; private set; }
    public string Accion { get; private set; } = default!;
    public string Motivo { get; private set; } = default!;
    public Guid UsuarioId { get; private set; }
    public DateTimeOffset Fecha { get; private set; }

    private BitacoraConciliacion() { }

    public BitacoraConciliacion(
        Guid conciliacionId,
        Guid? extractoLineaId,
        Guid? movimientoBancarioId,
        string accion,
        string motivo,
        Guid usuarioId,
        DateTimeOffset fecha)
        : base(Guid.CreateVersion7())
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("BITACORA_MOTIVO_VACIO", "El motivo es obligatorio para el registro en bitácora.");

        ConciliacionId = conciliacionId;
        ExtractoLineaId = extractoLineaId;
        MovimientoBancarioId = movimientoBancarioId;
        Accion = accion;
        Motivo = motivo.Trim();
        UsuarioId = usuarioId;
        Fecha = fecha;
    }
}
