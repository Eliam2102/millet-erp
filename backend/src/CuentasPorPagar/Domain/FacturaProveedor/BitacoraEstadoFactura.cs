using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.FacturaProveedor;

/// <summary>
/// Entrada de bitácora de transición de estados de
/// <see cref="FacturaProveedor"/> (§4.2 del 00-levantamiento). Persistida
/// como hija del agregado para trazabilidad fiscal y operativa.
/// </summary>
public sealed class BitacoraEstadoFactura : BaseEntity, IBelongsToAggregate, IAuditable
{
    public Guid FacturaProveedorId { get; private set; }
    public EstadoPasivo EstadoAnterior { get; private set; }
    public EstadoPasivo EstadoNuevo { get; private set; }
    public DateTimeOffset OcurridoEn { get; private set; }
    public string? Motivo { get; private set; }
    public Guid? UsuarioId { get; private set; }

    public Guid AggregateRootId => FacturaProveedorId;

    private BitacoraEstadoFactura() { }

    internal BitacoraEstadoFactura(
        Guid id,
        Guid facturaProveedorId,
        EstadoPasivo estadoAnterior,
        EstadoPasivo estadoNuevo,
        DateTimeOffset ocurridoEn,
        string? motivo,
        Guid? usuarioId) : base(id)
    {
        FacturaProveedorId = facturaProveedorId;
        EstadoAnterior = estadoAnterior;
        EstadoNuevo = estadoNuevo;
        OcurridoEn = ocurridoEn;
        Motivo = motivo;
        UsuarioId = usuarioId;
    }
}
