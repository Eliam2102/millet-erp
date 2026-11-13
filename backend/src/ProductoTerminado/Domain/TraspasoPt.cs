using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.Domain;

/// <summary>Datos de transporte del traspaso foráneo (los mismos que pide la hoja de salida para la Carta Porte). <c>Destino</c> es el texto del destino en la Carta Porte.</summary>
public sealed record TransporteTraspaso(string? Destino, Guid? VehiculoId, Guid? OperadorId, decimal? DistanciaKm, decimal? PesoKg,
    string? OrigenCodigoPostal, string? OrigenEstado, string? DestinoCodigoPostal, string? DestinoEstado);

/// <summary>
/// Traspaso de piezas entre sucursales (PT5.9, M-249, M-350): se prepara y documenta (las piezas no se mueven), lo autoriza otra persona (salen a tránsito),
/// se recibe en destino y las diferencias se clasifican y se aclaran hasta cerrarlo. Las pendientes de una recepción parcial quedan en tránsito.
/// </summary>
public sealed class TraspasoPt : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    private readonly List<TraspasoPtPieza> _piezas = [];

    public Guid EmpresaId { get; set; }
    public string Folio { get; private set; } = "";
    public Guid SucursalOrigenId { get; private set; }
    public Guid SucursalDestinoId { get; private set; }
    public EstadoTraspaso Estado { get; private set; }
    /// <summary>Cuándo se preparó el traspaso; la salida real a tránsito es <see cref="AutorizadoAt"/>.</summary>
    public DateTimeOffset SalidaAt { get; private set; }
    public DateTimeOffset? RecibidoAt { get; private set; }
    public Guid PreparadoPorId { get; private set; }
    public Guid? AutorizadoPorId { get; private set; }
    public DateTimeOffset? AutorizadoAt { get; private set; }
    /// <summary>Traslado foráneo o por carretera federal: al autorizar lleva Carta Porte.</summary>
    public bool Foranea { get; private set; }
    public string? Destino { get; private set; }
    public Guid? VehiculoId { get; private set; }
    public Guid? OperadorId { get; private set; }
    public decimal? DistanciaKm { get; private set; }
    public decimal? PesoKg { get; private set; }
    public string? OrigenCodigoPostal { get; private set; }
    public string? OrigenEstado { get; private set; }
    public string? DestinoCodigoPostal { get; private set; }
    public string? DestinoEstado { get; private set; }
    public Guid? CartaPorteId { get; private set; }
    public string? CartaPorteFolio { get; private set; }
    public IReadOnlyCollection<TraspasoPtPieza> Piezas => _piezas;

    private TraspasoPt() { }

    private TraspasoPt(Guid empresa, Guid origen, Guid destino, IEnumerable<Guid> piezaIds, Guid preparadoPor, DateTimeOffset ahora, DateOnly dia) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresa;
        Folio = $"TR-{dia:yyyyMMdd}-{Id.ToString("N")[^5..].ToUpperInvariant()}";
        SucursalOrigenId = origen;
        SucursalDestinoId = destino;
        Estado = EstadoTraspaso.Preparado;
        SalidaAt = ahora;
        PreparadoPorId = preparadoPor;
        _piezas.AddRange(piezaIds.Select(id => new TraspasoPtPieza(empresa, Id, id)));
    }

    public static TraspasoPt Crear(Guid empresa, Guid origen, Guid destino, IReadOnlyCollection<Guid> piezaIds, Guid preparadoPor, DateTimeOffset ahora,
        bool foranea = false, TransporteTraspaso? transporte = null, DateOnly? diaLocal = null)
    {
        if (origen == destino) throw new BusinessRuleException("PT_TRASPASO_MISMA_SUCURSAL", "El origen y el destino del traspaso deben ser distintos.");
        if (piezaIds.Count == 0 || piezaIds.Distinct().Count() != piezaIds.Count)
            throw new BusinessRuleException("PT_TRASPASO_PIEZAS_INVALIDAS", "El traspaso necesita al menos una pieza y sin repetir.");
        var t = new TraspasoPt(empresa, origen, destino, piezaIds, preparadoPor, ahora, diaLocal ?? DateOnly.FromDateTime(ahora.UtcDateTime));
        if (foranea)
        {
            t.Foranea = true;
            (t.Destino, t.VehiculoId, t.OperadorId, t.DistanciaKm, t.PesoKg) = (transporte?.Destino?.Trim(), transporte?.VehiculoId, transporte?.OperadorId, transporte?.DistanciaKm, transporte?.PesoKg);
            (t.OrigenCodigoPostal, t.OrigenEstado, t.DestinoCodigoPostal, t.DestinoEstado) =
                (transporte?.OrigenCodigoPostal?.Trim(), transporte?.OrigenEstado?.Trim().ToUpperInvariant(), transporte?.DestinoCodigoPostal?.Trim(), transporte?.DestinoEstado?.Trim().ToUpperInvariant());
            t.ExigirTransporteCompleto();
        }
        return t;
    }

    private void ExigirTransporteCompleto()
    {
        if (Foranea && (string.IsNullOrWhiteSpace(Destino) || VehiculoId is null || OperadorId is null || DistanciaKm is not > 0 || PesoKg is not > 0 || string.IsNullOrWhiteSpace(OrigenCodigoPostal)
            || string.IsNullOrWhiteSpace(OrigenEstado) || string.IsNullOrWhiteSpace(DestinoCodigoPostal) || string.IsNullOrWhiteSpace(DestinoEstado)))
            throw new BusinessRuleException("PT_TRASPASO_CARTA_PORTE_INCOMPLETA", "El traspaso foráneo necesita destino, vehículo, operador, distancia, peso, y código postal y estado de origen y destino para la Carta Porte.");
    }

    /// <summary>Solo se autoriza un traspaso preparado, por una persona distinta de quien lo preparó (separación de funciones) y con el transporte foráneo completo.</summary>
    public void ExigirAutorizable(Guid usuarioId)
    {
        if (Estado != EstadoTraspaso.Preparado) throw new BusinessRuleException("PT_TRASPASO_NO_PREPARADO", "Solo se autoriza un traspaso preparado.");
        if (usuarioId == PreparadoPorId) throw new BusinessRuleException("PT_TRASPASO_MISMO_USUARIO", "Quien prepara el traspaso no puede autorizarlo: lo autoriza otra persona.");
        ExigirTransporteCompleto();
    }

    /// <summary>La Carta Porte se guarda en cuanto sale, antes de terminar la autorización: si lo demás falla, reintentar no emite otra.</summary>
    public void AsignarCartaPorte(Guid id, string folio) => (CartaPorteId, CartaPorteFolio) = (id, folio);

    public void Autorizar(Guid usuarioId, DateTimeOffset ahora)
    {
        ExigirAutorizable(usuarioId);
        (Estado, AutorizadoPorId, AutorizadoAt) = (EstadoTraspaso.EnTransito, usuarioId, ahora);
    }

    /// <summary>Confirma la recepción de las piezas dadas (que deben seguir en tránsito). Con faltantes queda <c>ConDiferencia</c>; al llegar todas, <c>Recibido</c> (o <c>Cerrado</c> si hubo diferencias aclaradas).</summary>
    public void Recibir(IReadOnlyCollection<Guid> piezaIds, DateTimeOffset ahora)
    {
        ExigirEnRecepcion();
        var porRecibir = _piezas.Where(p => piezaIds.Contains(p.PiezaId)).ToList();
        if (piezaIds.Count == 0 || porRecibir.Count != piezaIds.Distinct().Count() || porRecibir.Any(p => !p.EnTransito))
            throw new BusinessRuleException("PT_TRASPASO_RECEPCION_INVALIDA", "Solo se reciben piezas de este traspaso que sigan en tránsito.");
        foreach (var p in porRecibir) p.MarcarRecibida();
        Reevaluar(ahora);
    }

    /// <summary>Anota qué le pasó a cada pieza que no llegó: faltante, dañada en tránsito o en cuarentena. El motivo es obligatorio y la evidencia también si llegó dañada.</summary>
    public void Clasificar(IReadOnlyCollection<(Guid PiezaId, ClasificacionDiferencia Clasificacion, string Motivo, string? Evidencia)> items)
    {
        ExigirEnRecepcion();
        if (items.Count == 0) throw new BusinessRuleException("PT_TRASPASO_CLASIFICACION_INVALIDA", "Indica al menos una pieza por clasificar.");
        foreach (var i in items)
        {
            var p = _piezas.FirstOrDefault(x => x.PiezaId == i.PiezaId);
            if (p is null || !p.EnTransito) throw new BusinessRuleException("PT_TRASPASO_CLASIFICACION_INVALIDA", "Solo se clasifican piezas de este traspaso que sigan en tránsito.");
            if (string.IsNullOrWhiteSpace(i.Motivo)) throw new BusinessRuleException("PT_TRASPASO_MOTIVO_REQUERIDO", "Cada pieza con diferencia necesita su motivo.");
            if (i.Clasificacion == ClasificacionDiferencia.DanadaEnTransito && string.IsNullOrWhiteSpace(i.Evidencia))
                throw new BusinessRuleException("PT_TRASPASO_EVIDENCIA_REQUERIDA", "La pieza dañada en tránsito necesita evidencia.");
            p.Clasificar(i.Clasificacion, i.Motivo.Trim(), string.IsNullOrWhiteSpace(i.Evidencia) ? null : i.Evidencia.Trim());
        }
    }

    /// <summary>Aclara una pieza clasificada: regresa al origen o se da por perdida. (Si llega tarde se usa <see cref="Recibir"/>, que también la aclara.) Al quedar todas resueltas el traspaso se cierra.</summary>
    public void Aclarar(Guid piezaId, ResolucionDiferencia resolucion, DateTimeOffset ahora)
    {
        ExigirEnRecepcion();
        var p = _piezas.FirstOrDefault(x => x.PiezaId == piezaId);
        if (p is null || !p.EnTransito) throw new BusinessRuleException("PT_TRASPASO_ACLARACION_INVALIDA", "Solo se aclaran piezas de este traspaso que sigan en tránsito.");
        if (p.Clasificacion is null) throw new BusinessRuleException("PT_TRASPASO_SIN_CLASIFICAR", "Clasifica la pieza (con su motivo) antes de aclararla.");
        if (resolucion == ResolucionDiferencia.RecibidaTarde) throw new BusinessRuleException("PT_TRASPASO_ACLARACION_INVALIDA", "La pieza que llega tarde se recibe con la recepción del traspaso.");
        p.Resolver(resolucion);
        Reevaluar(ahora);
    }

    private void ExigirEnRecepcion()
    {
        if (Estado is not (EstadoTraspaso.EnTransito or EstadoTraspaso.ConDiferencia))
            throw new BusinessRuleException("PT_TRASPASO_NO_AUTORIZADO", "El traspaso debe estar autorizado y en tránsito.");
    }

    private void Reevaluar(DateTimeOffset ahora)
    {
        if (_piezas.Any(p => p.EnTransito)) { Estado = EstadoTraspaso.ConDiferencia; return; }
        Estado = _piezas.Any(p => p.Clasificacion is not null) ? EstadoTraspaso.Cerrado : EstadoTraspaso.Recibido;
        RecibidoAt = ahora;
    }
}

public sealed class TraspasoPtPieza : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid TraspasoPtId { get; private set; }
    public Guid AggregateRootId => TraspasoPtId;
    public Guid PiezaId { get; private set; }
    public bool Recibida { get; private set; }
    public ClasificacionDiferencia? Clasificacion { get; private set; }
    public string? MotivoDiferencia { get; private set; }
    /// <summary>Referencia al blob de la evidencia de la diferencia.</summary>
    public string? EvidenciaDiferencia { get; private set; }
    public ResolucionDiferencia? Resolucion { get; private set; }
    /// <summary>Sigue en tránsito: ni llegó ni se aclaró.</summary>
    public bool EnTransito => !Recibida && Resolucion is null;

    private TraspasoPtPieza() { }

    internal TraspasoPtPieza(Guid empresa, Guid traspasoId, Guid piezaId) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresa;
        TraspasoPtId = traspasoId;
        PiezaId = piezaId;
    }

    /// <summary>Una pieza que ya tenía diferencia clasificada y llega después queda aclarada como recibida tarde.</summary>
    internal void MarcarRecibida()
    {
        Recibida = true;
        if (Clasificacion is not null) Resolucion = ResolucionDiferencia.RecibidaTarde;
    }

    internal void Clasificar(ClasificacionDiferencia clasificacion, string motivo, string? evidencia) => (Clasificacion, MotivoDiferencia, EvidenciaDiferencia) = (clasificacion, motivo, evidencia);

    internal void Resolver(ResolucionDiferencia resolucion) => Resolucion = resolucion;
}
