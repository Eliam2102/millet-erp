using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Conciliaciones;

/// <summary>
/// Vínculo de emparejamiento entre una línea de extracto y un movimiento bancario interno.
/// Soporta matching 1 a 1 y varios a 1 (varios movimientos internos cubiertos por una sola línea de extracto).
/// </summary>
public sealed class ExtractoLineaMatch : BaseEntity
{
    public Guid ExtractoLineaId { get; private set; }
    public Guid MovimientoBancarioId { get; private set; }
    public decimal Monto { get; private set; }
    public DateTimeOffset FechaMatch { get; private set; }
    public Guid EmparejadoPor { get; private set; }

    private ExtractoLineaMatch() { }

    public ExtractoLineaMatch(
        Guid extractoLineaId,
        Guid movimientoBancarioId,
        decimal monto,
        Guid emparejadoPor,
        DateTimeOffset fechaMatch)
        : base(Guid.CreateVersion7())
    {
        ExtractoLineaId = extractoLineaId;
        MovimientoBancarioId = movimientoBancarioId;
        Monto = monto;
        EmparejadoPor = emparejadoPor;
        FechaMatch = fechaMatch;
    }
}
