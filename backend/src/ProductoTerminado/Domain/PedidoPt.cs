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
