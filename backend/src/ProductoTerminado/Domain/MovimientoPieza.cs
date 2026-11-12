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

    private MovimientoPieza(PiezaPt pieza, TipoMovimientoPieza tipo, EstadoPieza anterior, EstadoPieza nuevo, Guid origen, Guid destino, Guid traspasoId, DateTimeOffset ocurridoAt, string usuario) : base(Guid.CreateVersion7())
    {
        EmpresaId = pieza.EmpresaId;
        PiezaId = pieza.Id;
        Tipo = tipo;
        SucursalOrigenId = origen;
        SucursalDestinoId = destino;
        EstadoAnterior = anterior.ToString();
        EstadoNuevo = nuevo.ToString();
        Referencia = traspasoId.ToString();
        OcurridoAt = ocurridoAt;
        Usuario = usuario;
    }

    /// <summary>Llamar antes de <c>SalirATransito</c>: lee la sucursal de origen de la pieza. Referencia = traspaso_id (idempotente por pieza y traspaso).</summary>
    private MovimientoPieza(PiezaPt pieza, TipoMovimientoPieza tipo, EstadoPieza anterior, string referencia, DateTimeOffset ocurridoAt, string usuario) : base(Guid.CreateVersion7())
    {
        EmpresaId = pieza.EmpresaId;
        PiezaId = pieza.Id;
        Tipo = tipo;
        SucursalOrigenId = pieza.SucursalId;
        EstadoAnterior = anterior.ToString();
        EstadoNuevo = pieza.Estado.ToString();
        Referencia = referencia;
        OcurridoAt = ocurridoAt;
        Usuario = usuario;
    }

    /// <summary>Se llama después de cambiar el estado de la pieza; <c>referencia</c> es el albarán.</summary>
    public static MovimientoPieza Paso(PiezaPt pieza, TipoMovimientoPieza tipo, EstadoPieza anterior, Guid albaranId, DateTimeOffset ocurridoAt, string usuario)
        => new(pieza, tipo, anterior, albaranId.ToString(), ocurridoAt, usuario);

    /// <summary>Carga pieza por pieza (PT5.4): la referencia lleva el instante, porque una pieza puede cargarse, descargarse y cargarse otra vez en el mismo albarán.</summary>
    public static MovimientoPieza PasoPieza(PiezaPt pieza, TipoMovimientoPieza tipo, EstadoPieza anterior, Guid albaranId, DateTimeOffset ocurridoAt, string usuario)
        => new(pieza, tipo, anterior, $"{albaranId}/{ocurridoAt.UtcTicks}", ocurridoAt, usuario);

    /// <summary>
    /// Salida por un albarán de A+W (el albarán vive en A+W): se llama después de embarcar; referencia =
    /// <c>aw-albaran:{pedidoAlbaran}:{folio}</c>, con el pedido de A+W que lleva el albarán para no depender de que
    /// <c>NR_LIEFERSCHEIN</c> sea único en todo A+W.
    /// </summary>
    public static MovimientoPieza SalidaAlbaranAw(PiezaPt pieza, EstadoPieza anterior, string pedidoAlbaran, int folio, DateTimeOffset ocurridoAt)
        => new(pieza, TipoMovimientoPieza.Salida, anterior, ReferenciaAlbaranAw(pedidoAlbaran, folio), ocurridoAt, "sistema");

    public static string ReferenciaAlbaranAw(string pedidoAlbaran, int folio) => $"aw-albaran:{pedidoAlbaran.Trim()}:{folio}";

    /// <summary>Reversa de la salida de una pieza por cancelación de albarán (PT5.8 / §11).</summary>
    public static MovimientoPieza SalidaCancelada(PiezaPt pieza, EstadoPieza anterior, Guid albaranId, string motivo, DateTimeOffset ocurridoAt, string usuario)
    {
        var referencia = string.IsNullOrWhiteSpace(motivo) ? albaranId.ToString() : $"{albaranId}:{motivo.Trim()}";
        if (referencia.Length > 200) referencia = referencia[..200];
        return new(pieza, TipoMovimientoPieza.SalidaCancelada, anterior, referencia, ocurridoAt, usuario);
    }

    public static MovimientoPieza TrasladoSalida(PiezaPt pieza, Guid destino, Guid traspasoId, DateTimeOffset ocurridoAt, string usuario)
        => new(pieza, TipoMovimientoPieza.TrasladoSalida, EstadoPieza.EnPT, EstadoPieza.EnTransito, pieza.SucursalId, destino, traspasoId, ocurridoAt, usuario);

    public static MovimientoPieza TrasladoRecepcion(PiezaPt pieza, Guid origen, Guid destino, Guid traspasoId, DateTimeOffset ocurridoAt, string usuario)
        => new(pieza, TipoMovimientoPieza.TrasladoRecepcion, EstadoPieza.EnTransito, EstadoPieza.EnPT, origen, destino, traspasoId, ocurridoAt, usuario);

    /// <summary>Aclaración del traspaso: la pieza que no llegó regresa al PT de origen.</summary>
    public static MovimientoPieza TrasladoRegreso(PiezaPt pieza, Guid origen, Guid destino, Guid traspasoId, DateTimeOffset ocurridoAt, string usuario)
        => new(pieza, TipoMovimientoPieza.TrasladoRegreso, EstadoPieza.EnTransito, EstadoPieza.EnPT, origen, destino, traspasoId, ocurridoAt, usuario);

    /// <summary>Aclaración del traspaso: la pieza se dio por perdida y queda en revisión para Calidad.</summary>
    public static MovimientoPieza TrasladoPerdida(PiezaPt pieza, Guid origen, Guid destino, Guid traspasoId, DateTimeOffset ocurridoAt, string usuario)
        => new(pieza, TipoMovimientoPieza.TrasladoPerdida, EstadoPieza.EnTransito, EstadoPieza.EnRevision, origen, destino, traspasoId, ocurridoAt, usuario);

    public static MovimientoPieza Alta(PiezaPt pieza, string referencia, DateTimeOffset ocurridoAt)
        => new(pieza, TipoMovimientoPieza.Alta, null, referencia, ocurridoAt);

    /// <summary>La pieza se volvió a terminar en A+W con la misma etiqueta (rota y repuesta): es un hecho nuevo y queda en su historia (Jorge E08-2).</summary>
    public static MovimientoPieza Reposicion(PiezaPt pieza, string referencia, DateTimeOffset ocurridoAt)
        => new(pieza, TipoMovimientoPieza.Reposicion, pieza.Estado.ToString(), referencia, ocurridoAt);
}
