using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.Domain;

/// <summary>Copia local del pedido facturable que sirve de referencia para contar piezas esperadas.</summary>
public sealed class PedidoPt : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    private readonly List<PedidoPtPosicion> _posiciones = [];

    public Guid EmpresaId { get; set; }
    public Guid PedidoFacturableId { get; private set; }
    public string NumeroPedido { get; private set; } = "";
    public Guid SucursalVentaId { get; private set; }
    public Guid? SucursalEntregaId { get; private set; }
    public string? CondicionPagoOrigen { get; private set; }
    public long? VersionOrigen { get; private set; }
    /// <summary>Null hasta que el pedido está completo en PT (P-096). Independiente del avance físico (E6).</summary>
    public EstadoLiberacion? Liberacion { get; private set; }
    public string? MotivoLiberacion { get; private set; }
    public DateTimeOffset? LiberacionAt { get; private set; }
    /// <summary>Primera salida emitida: el pedido pasa a 71 (en ruta). No cambia con las salidas siguientes.</summary>
    public DateTimeOffset? EnRutaAt { get; private set; }
    /// <summary>PT5.6: todas las piezas esperadas del pedido fueron recibidas por el cliente.</summary>
    public DateTimeOffset? EntregadoAt { get; private set; }
    /// <summary>PT5.6: Facturación ya tiene un CFDI vigente del pedido.</summary>
    public DateTimeOffset? FacturadoAt { get; private set; }
    /// <summary>PT5.6: 115 = facturado y entregado (Manual, Anexo A). Se fija una sola vez.</summary>
    public DateTimeOffset? Estado115At { get; private set; }
    public IReadOnlyCollection<PedidoPtPosicion> Posiciones => _posiciones;

    private PedidoPt() { }

    private PedidoPt(Guid empresa, Guid pedidoFacturableId, string numero, Guid sucursalVenta, string? condicionPago, long? versionOrigen) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresa;
        PedidoFacturableId = pedidoFacturableId;
        NumeroPedido = numero.Trim();
        SucursalVentaId = sucursalVenta;
        CondicionPagoOrigen = condicionPago;
        VersionOrigen = versionOrigen;
    }

    public static PedidoPt Crear(Guid empresa, Guid pedidoFacturableId, string numero, Guid sucursalVenta, string? condicionPago, long? versionOrigen)
        => new(empresa, pedidoFacturableId, numero, sucursalVenta, condicionPago, versionOrigen);

    /// <summary>Entra a 69 con el pedido completo. Devuelve false si ya tenía estado (una sola entrada).</summary>
    public bool Retener(DateTimeOffset ahora)
    {
        if (Liberacion is not null) return false;
        (Liberacion, MotivoLiberacion, LiberacionAt) = (EstadoLiberacion.Retenido, MotivosPt.SaldoPendiente, ahora);
        return true;
    }

    /// <summary>Pasa a 70 con su motivo. Devuelve false si ya estaba liberado (un solo cambio, aunque llegue repetido).</summary>
    public bool MarcarEnRuta(DateTimeOffset ahora)
    {
        if (EnRutaAt is not null) return false;
        EnRutaAt = ahora;
        return true;
    }

    public bool MarcarEntregado(DateTimeOffset ahora)
    {
        if (EntregadoAt is not null) return false;
        EntregadoAt = ahora;
        return true;
    }

    public bool MarcarFacturado(DateTimeOffset ahora)
    {
        if (FacturadoAt is not null) return false;
        FacturadoAt = ahora;
        return true;
    }

    /// <summary>Pasa a 115 cuando ya está facturado y entregado. Devuelve true solo la primera vez.</summary>
    public bool Cerrar115(DateTimeOffset ahora)
    {
        if (Estado115At is not null || EntregadoAt is null || FacturadoAt is null) return false;
        Estado115At = ahora;
        return true;
    }

    public bool Liberar(string motivo, DateTimeOffset ahora)
    {
        if (Liberacion == EstadoLiberacion.Liberado) return false;
        (Liberacion, MotivoLiberacion, LiberacionAt) = (EstadoLiberacion.Liberado, motivo, ahora);
        return true;
    }

    /// <summary>
    /// Upsert por posición con la regla de piezas esperadas: clave SAT E48 (servicio) = 0;
    /// cantidad no entera = 0 y se marca; en otro caso la cantidad entera.
    /// Nunca elimina posiciones (las que ya tienen piezas no pueden desaparecer).
    /// </summary>
    public void ActualizarPosiciones(long? versionOrigen, IEnumerable<(int Posicion, Guid? ProductoId, string Descripcion, decimal Cantidad, string? ClaveUnidadSat)> lineas)
    {
        foreach (var l in lineas)
        {
            var (esperadas, noEntera) = CalcularEsperadas(l.Cantidad, l.ClaveUnidadSat);
            var existente = _posiciones.FirstOrDefault(p => p.Posicion == l.Posicion);
            if (existente is null)
                _posiciones.Add(new PedidoPtPosicion(EmpresaId, Id, l.Posicion, l.ProductoId, l.Descripcion, esperadas, noEntera));
            else
                existente.Actualizar(l.ProductoId, l.Descripcion, esperadas, noEntera);
        }
        VersionOrigen = versionOrigen;
    }

    private static (int Esperadas, bool NoEntera) CalcularEsperadas(decimal cantidad, string? claveUnidadSat)
    {
        if (string.Equals(claveUnidadSat, "E48", StringComparison.OrdinalIgnoreCase)) return (0, false);
        if (cantidad != decimal.Truncate(cantidad)) return (0, true);
        return ((int)cantidad, false);
    }
}
