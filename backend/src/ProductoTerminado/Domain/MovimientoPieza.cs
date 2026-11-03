using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.Domain;

/// <summary>Bitácora de movimientos de una pieza; la referencia (id del escaneo) hace idempotente el alta.</summary>
public sealed class MovimientoPieza : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid PiezaId { get; private set; }
    public Guid AggregateRootId => PiezaId;
    public TipoMovimientoPieza Tipo { get; private set; }
    public Guid? SucursalOrigenId { get; private set; }
    public Guid? SucursalDestinoId { get; private set; }
    public string? EstadoAnterior { get; private set; }
    public string EstadoNuevo { get; private set; } = "";
    public string Referencia { get; private set; } = "";
    public DateTimeOffset OcurridoAt { get; private set; }
    public string Usuario { get; private set; } = "";

    private MovimientoPieza() { }

    private MovimientoPieza(PiezaPt pieza, TipoMovimientoPieza tipo, string? estadoAnterior, string referencia, DateTimeOffset ocurridoAt) : base(Guid.CreateVersion7())
    {
        EmpresaId = pieza.EmpresaId;
        PiezaId = pieza.Id;
        Tipo = tipo;
        SucursalDestinoId = pieza.SucursalId;
        EstadoAnterior = estadoAnterior;
        EstadoNuevo = EstadoPieza.EnPT.ToString();
        Referencia = referencia;
        OcurridoAt = ocurridoAt;
        Usuario = "sistema";
    }

    public static MovimientoPieza Alta(PiezaPt pieza, string referencia, DateTimeOffset ocurridoAt)
        => new(pieza, TipoMovimientoPieza.Alta, null, referencia, ocurridoAt);

    /// <summary>La pieza se volvió a terminar en A+W con la misma etiqueta (rota y repuesta): es un hecho nuevo y queda en su historia (Jorge E08-2).</summary>
    public static MovimientoPieza Reposicion(PiezaPt pieza, string referencia, DateTimeOffset ocurridoAt)
        => new(pieza, TipoMovimientoPieza.Reposicion, pieza.Estado.ToString(), referencia, ocurridoAt);
}
