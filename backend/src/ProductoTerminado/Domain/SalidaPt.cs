using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.Domain;

/// <summary>
/// Hoja de salida (PT5.5): lo que el chofer lleva en una ruta. Cada pedido de la hoja tiene su albarán, en el orden de entrega (secuencia).
/// Se emite una sola vez: emitir de nuevo no descuenta nada.
/// </summary>
public sealed class SalidaPt : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    private readonly List<AlbaranPt> _albaranes = [];

    public Guid EmpresaId { get; set; }
    public string Folio { get; private set; } = "";
    public Guid SucursalId { get; private set; }
    public string Destino { get; private set; } = "";
    /// <summary>Entrega foránea o por carretera federal: al emitir lleva Carta Porte (una por tramo).</summary>
    public bool Foranea { get; private set; }
    public Guid? VehiculoId { get; private set; }
    public Guid? OperadorId { get; private set; }
    public decimal? DistanciaKm { get; private set; }
    public decimal? PesoKg { get; private set; }
    public string? OrigenCodigoPostal { get; private set; }
    /// <summary>Clave SAT del estado (c_Estado, 3 letras), igual que el de destino.</summary>
    public string? OrigenEstado { get; private set; }
    public string? DestinoCodigoPostal { get; private set; }
    public string? DestinoEstado { get; private set; }
    public EstadoSalida Estado { get; private set; }
    public DateTimeOffset CreadaAt { get; private set; }
    public DateTimeOffset? EmitidaAt { get; private set; }
    public Guid? CartaPorteId { get; private set; }
    public string? CartaPorteFolio { get; private set; }
    public IReadOnlyCollection<AlbaranPt> Albaranes => _albaranes;

    private SalidaPt() { }
    private SalidaPt(Guid id) : base(id) { }

    public static SalidaPt Crear(Guid empresa, Guid sucursalId, string destino, bool foranea, Guid? vehiculoId, Guid? operadorId, decimal? distanciaKm, decimal? pesoKg,
        string? origenCp, string? origenEstado, string? destinoCp, string? destinoEstado, DateTimeOffset ahora)
    {
        if (foranea && (vehiculoId is null || operadorId is null || distanciaKm is not > 0 || pesoKg is not > 0 || string.IsNullOrWhiteSpace(origenCp) || string.IsNullOrWhiteSpace(origenEstado) || string.IsNullOrWhiteSpace(destinoCp) || string.IsNullOrWhiteSpace(destinoEstado)))
            throw new BusinessRuleException("PT_SALIDA_CARTA_PORTE_INCOMPLETA", "La salida foránea necesita vehículo, operador, distancia, peso, y código postal y estado de origen y destino para la Carta Porte.");
        var id = Guid.CreateVersion7();
        return new SalidaPt(id)
        {
            EmpresaId = empresa, Folio = $"HS-{ahora:yyyyMMdd}-{id.ToString("N")[^5..].ToUpperInvariant()}", SucursalId = sucursalId, Destino = destino.Trim(), Foranea = foranea,
            VehiculoId = vehiculoId, OperadorId = operadorId, DistanciaKm = distanciaKm, PesoKg = pesoKg, OrigenCodigoPostal = origenCp?.Trim(), OrigenEstado = origenEstado?.Trim().ToUpperInvariant(), DestinoCodigoPostal = destinoCp?.Trim(), DestinoEstado = destinoEstado?.Trim().ToUpperInvariant(),
            Estado = EstadoSalida.Borrador, CreadaAt = ahora,
        };
    }

    public AlbaranPt AgregarAlbaran(PedidoPt pedido, IReadOnlyCollection<Guid> piezaIds)
    {
        var albaran = new AlbaranPt(EmpresaId, Id, pedido.Id, _albaranes.Count + 1, $"{Folio}/{_albaranes.Count + 1}", piezaIds);
        _albaranes.Add(albaran);
        return albaran;
    }

    /// <summary>La Carta Porte se guarda en cuanto sale, antes de terminar la emisión: si lo demás falla, reintentar no emite otra.</summary>
    public void AsignarCartaPorte(Guid id, string folio) => (CartaPorteId, CartaPorteFolio) = (id, folio);

    /// <summary>Devuelve false si ya estaba emitida (no hay nada que aplicar de nuevo).</summary>
    public bool Emitir(DateTimeOffset ahora)
    {
        if (Estado == EstadoSalida.Emitida) return false;
        if (Estado == EstadoSalida.Cancelada) throw new BusinessRuleException("PT_SALIDA_CANCELADA", "La hoja de salida está cancelada.");
        (Estado, EmitidaAt) = (EstadoSalida.Emitida, ahora);
        foreach (var a in _albaranes.Where(a => a.Piezas.Count > 0)) a.Emitir(ahora);
        return true;
    }

    public void Cancelar()
    {
        if (Estado != EstadoSalida.Borrador) throw new BusinessRuleException("PT_SALIDA_NO_CANCELABLE", "Solo se cancela una hoja de salida que aún no se emite.");
        if (CartaPorteId is not null) throw new BusinessRuleException("PT_SALIDA_CON_CARTA_PORTE", "La hoja ya tiene Carta Porte timbrada; se cancela primero en Facturación.");
        Estado = EstadoSalida.Cancelada;
    }

    /// <summary>Cancela un albarán emitido o en borrador de la hoja de salida (PT5.8 / §11).</summary>
    public void CancelarAlbaran(Guid albaranId, string motivo, Guid usuarioId, DateTimeOffset ahora)
    {
        var albaran = _albaranes.FirstOrDefault(a => a.Id == albaranId)
            ?? throw new BusinessRuleException("PT_ALBARAN_NO_PERTENECE_A_SALIDA", "El albarán no pertenece a esta hoja de salida.");
        if (albaran.Cancelado) throw new BusinessRuleException("PT_ALBARAN_YA_CANCELADO", "El albarán ya está cancelado.");
        if (Estado == EstadoSalida.Cancelada) throw new BusinessRuleException("PT_SALIDA_CANCELADA", "La hoja de salida está cancelada.");
        albaran.Cancelar(motivo, usuarioId, ahora);
        if (_albaranes.Count > 0 && _albaranes.All(a => a.Cancelado)) Estado = EstadoSalida.Cancelada;
    }
}

/// <summary>Albarán por pedido: las piezas cargadas de ese pedido en la hoja.</summary>
public sealed class AlbaranPt : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    private readonly List<AlbaranPtPieza> _piezas = [];

    public Guid EmpresaId { get; set; }
    public Guid SalidaPtId { get; private set; }
    public Guid AggregateRootId => SalidaPtId;
    public Guid PedidoPtId { get; private set; }
    public int Secuencia { get; private set; }
    public string Folio { get; private set; } = "";
    public DateTimeOffset? EmitidoAt { get; private set; }
    public DateTimeOffset? CanceladoAt { get; private set; }
    public string? MotivoCancelacion { get; private set; }
    public Guid? CanceladoPorId { get; private set; }
    public bool Cancelado => CanceladoAt is not null;
    public DateTimeOffset? EntregadoAt { get; private set; }
    public bool Entregado => EntregadoAt is not null;
    public DateTimeOffset? LiquidadoAt { get; private set; }
    public bool Liquidado => LiquidadoAt is not null;
    public IReadOnlyCollection<AlbaranPtPieza> Piezas => _piezas;

    private AlbaranPt() { }

    internal AlbaranPt(Guid empresa, Guid salidaId, Guid pedidoPtId, int secuencia, string folio, IEnumerable<Guid> piezaIds) : base(Guid.CreateVersion7())
    {
        (EmpresaId, SalidaPtId, PedidoPtId, Secuencia, Folio) = (empresa, salidaId, pedidoPtId, secuencia, folio);
        _piezas.AddRange(piezaIds.Select(id => new AlbaranPtPieza(empresa, Id, id)));
    }

    internal void Emitir(DateTimeOffset ahora) => EmitidoAt = ahora;

    internal void MarcarEntregado(DateTimeOffset ahora) => EntregadoAt = ahora;

    internal void MarcarLiquidado(DateTimeOffset ahora) => LiquidadoAt = ahora;

    internal void Cancelar(string motivo, Guid usuarioId, DateTimeOffset ahora)
    {
        if (Cancelado) throw new BusinessRuleException("PT_ALBARAN_YA_CANCELADO", "El albarán ya está cancelado.");
        if (Entregado) throw new BusinessRuleException("PT_ALBARAN_YA_ENTREGADO", "El albarán ya fue entregado en ruta; no se puede cancelar.");
        if (Liquidado) throw new BusinessRuleException("PT_ALBARAN_YA_LIQUIDADO", "El albarán ya fue liquidado en caja; no se puede cancelar.");
        (CanceladoAt, MotivoCancelacion, CanceladoPorId) = (ahora, motivo?.Trim(), usuarioId);
    }
}

public sealed class AlbaranPtPieza : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid AlbaranPtId { get; private set; }
    public Guid AggregateRootId => AlbaranPtId;
    public Guid PiezaId { get; private set; }

    private AlbaranPtPieza() { }

    internal AlbaranPtPieza(Guid empresa, Guid albaranId, Guid piezaId) : base(Guid.CreateVersion7())
        => (EmpresaId, AlbaranPtId, PiezaId) = (empresa, albaranId, piezaId);
}
