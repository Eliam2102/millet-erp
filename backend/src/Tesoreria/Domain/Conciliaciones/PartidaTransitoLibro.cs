using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Conciliaciones;

/// <summary>
/// M-201 (Javier Canché, 11-nov): movimiento en libros que el banco todavía no refleja al cierre del mes y queda en tránsito:
/// cheque no cobrado o depósito del último día del mes. Lo autoriza el Director de Planeación Financiera y se concilia el mes
/// siguiente. <see cref="Monto"/> lleva signo (ingreso positivo, egreso negativo) y se descuenta del saldo en libros al cerrar.
/// </summary>
public sealed class PartidaTransitoLibro : BaseEntity, IAuditable
{
    public Guid ConciliacionId { get; private set; }
    public Guid MovimientoBancarioId { get; private set; }
    public DateOnly FechaValor { get; private set; }
    public string? Referencia { get; private set; }
    public decimal Monto { get; private set; }
    public PartidaTransitoTipo Tipo { get; private set; }
    public string Causa { get; private set; } = default!;
    public DateOnly FechaPrevista { get; private set; }
    public Guid AutorizadaPor { get; private set; }
    public DateTimeOffset AutorizadaEn { get; private set; }

    private PartidaTransitoLibro() { }

    internal PartidaTransitoLibro(Guid conciliacionId, Guid movimientoId, DateOnly fechaValor, string? referencia, decimal monto,
        PartidaTransitoTipo tipo, string causa, DateOnly fechaPrevista, Guid autorizadaPor, DateTimeOffset ahora) : base(Guid.CreateVersion7())
    {
        ConciliacionId = conciliacionId; MovimientoBancarioId = movimientoId; FechaValor = fechaValor; Referencia = referencia;
        Monto = monto; Tipo = tipo; Causa = causa; FechaPrevista = fechaPrevista; AutorizadaPor = autorizadaPor; AutorizadaEn = ahora;
    }
}
