using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.Domain;

/// <summary>Pieza física de producto terminado. Sin estatus de A+W: PT solo conoce su propio estado (D7).</summary>
public sealed class PiezaPt : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid PedidoPtId { get; private set; }
    public Guid AggregateRootId => PedidoPtId;
    public int Posicion { get; private set; }
    public int NumeroPieza { get; private set; }
    public string Etiqueta { get; private set; } = "";
    public Guid SucursalId { get; private set; }
    public EstadoPieza Estado { get; private set; }
    public DateTimeOffset EscaneadaAt { get; private set; }
    public Guid EscaneoId { get; private set; }
    /// <summary>M-251: null = «por ubicar» (escaneada, sin confirmar dónde quedó físicamente).</summary>
    public AlmacenPt? Almacen { get; private set; }
    /// <summary>M-251: lugar dentro del almacén (pirámide, caballete, zona), opcional.</summary>
    public string? Lugar { get; private set; }
    public DateTimeOffset? UbicadaAt { get; private set; }
    public Guid? UbicadaPorId { get; private set; }
    /// <summary>M-251: costo de la pieza; null = costo pendiente hasta la valuación de PT (F1-PT-09).</summary>
    public decimal? Costo { get; private set; }

    private PiezaPt() { }

    private PiezaPt(Guid empresa, Guid pedidoPtId, int posicion, int numeroPieza, string etiqueta, Guid sucursalId, DateTimeOffset escaneadaAt, Guid escaneoId) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresa;
        PedidoPtId = pedidoPtId;
        Posicion = posicion;
        NumeroPieza = numeroPieza;
        Etiqueta = etiqueta.Trim();
        SucursalId = sucursalId;
        Estado = EstadoPieza.EnPT;
        EscaneadaAt = escaneadaAt;
        EscaneoId = escaneoId;
    }

    public static PiezaPt Alta(Guid empresa, PedidoPt pedido, int posicion, int numeroPieza, string etiqueta, Guid sucursalId, DateTimeOffset escaneadaAt, Guid escaneoId)
        => new(empresa, pedido.Id, posicion, numeroPieza, etiqueta, sucursalId, escaneadaAt, escaneoId);

    /// <summary>Sale a tránsito; conserva su sucursal de origen hasta que se reciba.</summary>
    public void SalirATransito()
    {
        if (Estado != EstadoPieza.EnPT) throw new BusinessRuleException("PT_PIEZA_NO_EN_PT", $"La pieza {Etiqueta} no está en PT.");
        Estado = EstadoPieza.EnTransito;
    }

    /// <summary>Entra al PT de la sucursal destino.</summary>
    public void Recibir(Guid sucursalDestinoId)
    {
        if (Estado != EstadoPieza.EnTransito) throw new BusinessRuleException("PT_PIEZA_NO_EN_TRANSITO", $"La pieza {Etiqueta} no está en tránsito.");
        Estado = EstadoPieza.EnPT;
        SucursalId = sucursalDestinoId;
        QuitarUbicacion();
    }

    /// <summary>M-251: confirma dónde quedó la pieza dentro de PT. Se puede reubicar mientras siga en PT.</summary>
    public void Ubicar(AlmacenPt almacen, string? lugar, Guid actor, DateTimeOffset ahora)
    {
        if (Estado != EstadoPieza.EnPT) throw new BusinessRuleException("PT_PIEZA_NO_EN_PT", $"La pieza {Etiqueta} no está en PT: solo se ubica lo que está físicamente aquí.");
        var limpio = string.IsNullOrWhiteSpace(lugar) ? null : lugar.Trim();
        if (limpio is { Length: > 100 }) throw new BusinessRuleException("PT_UBICACION_INVALIDA", "El lugar dentro del almacén tiene máximo 100 caracteres.");
        (Almacen, Lugar, UbicadaAt, UbicadaPorId) = (almacen, limpio, ahora, actor);
    }

    private void QuitarUbicacion() => (Almacen, Lugar, UbicadaAt, UbicadaPorId) = (null, null, null, null);

    /// <summary>M-351: queda segregada en revisión mientras Calidad dictamina (desde PT, rota al cargar, rechazada en sitio o devuelta por el cliente).</summary>
    public void SegregarPorDano()
    {
        if (Estado is not (EstadoPieza.EnPT or EstadoPieza.EnRevision or EstadoPieza.Entregada))
            throw new BusinessRuleException("PT_PIEZA_NO_SEGREGABLE", $"La pieza {Etiqueta} está {Estado}: solo se registra daño de piezas en PT, rechazadas en sitio o entregadas que regresaron.");
        Estado = EstadoPieza.EnRevision;
        QuitarUbicacion();
    }

    /// <summary>M-351: aplica el dictamen de Calidad sobre una pieza en revisión.</summary>
    public void AplicarDictamen(DictamenCalidadPt dictamen)
    {
        if (Estado != EstadoPieza.EnRevision) throw new BusinessRuleException("PT_PIEZA_NO_EN_REVISION", $"La pieza {Etiqueta} no está en revisión.");
        Estado = dictamen switch
        {
            DictamenCalidadPt.RegresarDisponible => EstadoPieza.EnPT,
            DictamenCalidadPt.Reproceso => EstadoPieza.EnReproceso,
            _ => EstadoPieza.Baja,
        };
    }

    /// <summary>A+W la volvió a terminar con la misma etiqueta (reproceso o reposición de una baja): regresa a PT por ubicar.</summary>
    public bool ReingresarPorReposicion(Guid sucursalId)
    {
        if (Estado is not (EstadoPieza.EnReproceso or EstadoPieza.Baja)) return false;
        Estado = EstadoPieza.EnPT;
        SucursalId = sucursalId;
        QuitarUbicacion();
        return true;
    }

    /// <summary>M-248: corrige el resultado de la entrega confirmada (entregada ↔ rechazada en sitio).</summary>
    public void CorregirEntrega(bool rechazada)
    {
        if (Estado is not (EstadoPieza.Entregada or EstadoPieza.EnRevision)) throw new BusinessRuleException("PT_PIEZA_NO_ENTREGADA", $"La pieza {Etiqueta} no está entregada ni rechazada en sitio.");
        Estado = rechazada ? EstadoPieza.EnRevision : EstadoPieza.Entregada;
    }

    /// <summary>Entra al camión. La pieza que PT5.4 ya cargó se acepta tal cual.</summary>
    public void Cargar()
    {
        if (Estado is not (EstadoPieza.EnPT or EstadoPieza.Cargada)) throw new BusinessRuleException("PT_PIEZA_NO_CARGABLE", $"La pieza {Etiqueta} no está en PT.");
        Estado = EstadoPieza.Cargada;
    }

    public void RevertirCarga()
    {
        if (Estado != EstadoPieza.Cargada) throw new BusinessRuleException("PT_PIEZA_NO_CARGADA", $"La pieza {Etiqueta} no está cargada.");
        Estado = EstadoPieza.EnPT;
    }

    public void Embarcar()
    {
        if (Estado != EstadoPieza.Cargada) throw new BusinessRuleException("PT_PIEZA_NO_CARGADA", $"La pieza {Etiqueta} no está cargada.");
        Estado = EstadoPieza.Embarcada;
    }

    /// <summary>Cancela la salida o albarán: regresa la pieza embarcada o cargada a EnPT.</summary>
    public void RevertirSalida()
    {
        if (Estado is not (EstadoPieza.Embarcada or EstadoPieza.Cargada)) throw new BusinessRuleException("PT_PIEZA_NO_SALIDA", $"La pieza {Etiqueta} no está embarcada ni cargada.");
        // Lo que ya había salido regresa al andén: se vuelve a ubicar (M-251).
        if (Estado == EstadoPieza.Embarcada) QuitarUbicacion();
        Estado = EstadoPieza.EnPT;
    }

    /// <summary>El cliente la recibió.</summary>
    public void Entregar()
    {
        if (Estado != EstadoPieza.Embarcada) throw new BusinessRuleException("PT_PIEZA_NO_EMBARCADA", $"La pieza {Etiqueta} no salió en una hoja emitida.");
        Estado = EstadoPieza.Entregada;
    }

    /// <summary>El cliente la rechazó en sitio: regresa en revisión (PT5.8 decide su destino).</summary>
    public void RechazarEnSitio()
    {
        if (Estado != EstadoPieza.Embarcada) throw new BusinessRuleException("PT_PIEZA_NO_EMBARCADA", $"La pieza {Etiqueta} no salió en una hoja emitida.");
        Estado = EstadoPieza.EnRevision;
    }
}
