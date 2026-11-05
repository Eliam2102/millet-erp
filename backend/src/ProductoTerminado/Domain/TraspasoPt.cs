using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.Domain;

/// <summary>Traspaso de piezas entre sucursales (PT5.9): sale de origen, viaja en tránsito y se recibe en destino. Las pendientes de una recepción parcial quedan en tránsito.</summary>
public sealed class TraspasoPt : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    private readonly List<TraspasoPtPieza> _piezas = [];

    public Guid EmpresaId { get; set; }
    public Guid SucursalOrigenId { get; private set; }
    public Guid SucursalDestinoId { get; private set; }
    public EstadoTraspaso Estado { get; private set; }
    public DateTimeOffset SalidaAt { get; private set; }
    public DateTimeOffset? RecibidoAt { get; private set; }
    public IReadOnlyCollection<TraspasoPtPieza> Piezas => _piezas;

    private TraspasoPt() { }

    private TraspasoPt(Guid empresa, Guid origen, Guid destino, IEnumerable<Guid> piezaIds, DateTimeOffset salidaAt) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresa;
        SucursalOrigenId = origen;
        SucursalDestinoId = destino;
        Estado = EstadoTraspaso.EnTransito;
        SalidaAt = salidaAt;
        _piezas.AddRange(piezaIds.Select(id => new TraspasoPtPieza(empresa, Id, id)));
    }

    public static TraspasoPt Crear(Guid empresa, Guid origen, Guid destino, IReadOnlyCollection<Guid> piezaIds, DateTimeOffset salidaAt)
    {
        if (origen == destino) throw new BusinessRuleException("PT_TRASPASO_MISMA_SUCURSAL", "El origen y el destino del traspaso deben ser distintos.");
        if (piezaIds.Count == 0 || piezaIds.Distinct().Count() != piezaIds.Count)
            throw new BusinessRuleException("PT_TRASPASO_PIEZAS_INVALIDAS", "El traspaso necesita al menos una pieza y sin repetir.");
        return new(empresa, origen, destino, piezaIds, salidaAt);
    }

    /// <summary>Confirma la recepción de las piezas dadas (que deben seguir en tránsito). Con faltantes queda <c>ConDiferencia</c>; al llegar todas, <c>Recibido</c>.</summary>
    public void Recibir(IReadOnlyCollection<Guid> piezaIds, DateTimeOffset ahora)
    {
        var porRecibir = _piezas.Where(p => piezaIds.Contains(p.PiezaId)).ToList();
        if (piezaIds.Count == 0 || porRecibir.Count != piezaIds.Distinct().Count() || porRecibir.Any(p => p.Recibida))
            throw new BusinessRuleException("PT_TRASPASO_RECEPCION_INVALIDA", "Solo se reciben piezas de este traspaso que sigan en tránsito.");
        foreach (var p in porRecibir) p.MarcarRecibida();
        var completo = _piezas.All(p => p.Recibida);
        Estado = completo ? EstadoTraspaso.Recibido : EstadoTraspaso.ConDiferencia;
        if (completo) RecibidoAt = ahora;
    }
}

public sealed class TraspasoPtPieza : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid TraspasoPtId { get; private set; }
    public Guid AggregateRootId => TraspasoPtId;
    public Guid PiezaId { get; private set; }
    public bool Recibida { get; private set; }

    private TraspasoPtPieza() { }

    internal TraspasoPtPieza(Guid empresa, Guid traspasoId, Guid piezaId) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresa;
        TraspasoPtId = traspasoId;
        PiezaId = piezaId;
    }

    internal void MarcarRecibida() => Recibida = true;
}
