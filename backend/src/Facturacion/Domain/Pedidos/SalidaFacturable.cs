using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Pedidos;

/// <summary>De dónde viene la salida parcial.</summary>
public enum OrigenSalidaFacturable : short
{
    /// <summary>Salida de Producto terminado (PT-01/PT-03).</summary>
    ProductoTerminado = 1,

    /// <summary>Contenedor de exportación de Comercio Exterior (CE-02).</summary>
    ContenedorExportacion = 2,

    /// <summary>Registrada a mano mientras no existen PT ni CE en el ERP.</summary>
    Manual = 3,
}

/// <summary>
/// FAC-08 (86e3qdddf, opción A): una salida parcial del pedido que se factura por sí sola.
/// El pedido original no se modifica (junta 12-oct): cada salida guarda su número de
/// parcialidad, piezas y cantidades, y se factura una sola vez. Lo facturado del pedido se
/// calcula con sus salidas. La suma de cantidades de las salidas nunca rebasa lo pedido.
/// </summary>
public sealed class SalidaFacturable : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public const int ReferenciaMaxLength = 80;

    public Guid EmpresaId { get; set; }
    public Guid PedidoFacturableId { get; private set; }

    /// <summary>1, 2, 3… por pedido. Supuesto a validar con Jorge (numeración de A+W).</summary>
    public int NumeroParcialidad { get; private set; }

    public OrigenSalidaFacturable Origen { get; private set; }

    /// <summary>Identificador de la salida en su origen (folio de salida PT o contenedor). Clave de idempotencia.</summary>
    public string Referencia { get; private set; } = string.Empty;

    public DateTimeOffset RegistradaEn { get; private set; }
    public decimal Total { get; private set; }

    private readonly List<SalidaFacturableLinea> _lineas = [];
    public IReadOnlyCollection<SalidaFacturableLinea> Lineas => _lineas.AsReadOnly();

    private SalidaFacturable() { }

    private SalidaFacturable(Guid id, Guid empresaId, Guid pedidoId, int numero, OrigenSalidaFacturable origen,
        string referencia, DateTimeOffset registradaEn) : base(id)
    {
        EmpresaId = empresaId;
        PedidoFacturableId = pedidoId;
        NumeroParcialidad = numero;
        Origen = origen;
        Referencia = referencia;
        RegistradaEn = registradaEn;
    }

    /// <summary>
    /// Registra la siguiente parcialidad del pedido. El importe de cada línea sale del precio
    /// de la línea del pedido; el descuento se reparte en proporción y la parcialidad que
    /// completa una posición se lleva el resto, así la suma de las facturas cuadra con el pedido.
    /// </summary>
    public static SalidaFacturable Registrar(
        PedidoFacturable pedido,
        IReadOnlyCollection<SalidaFacturable> previas,
        OrigenSalidaFacturable origen,
        string referencia,
        IReadOnlyList<(int Posicion, decimal Cantidad)> lineas,
        DateTimeOffset ahora)
    {
        if (pedido.Estado != EstadoPedidoFacturable.Importado)
            throw new BusinessRuleException("SALIDA_PEDIDO_NO_FACTURABLE",
                $"El pedido está en estado {pedido.Estado}; solo se registran salidas de un pedido sin factura completa.");
        if (string.IsNullOrWhiteSpace(referencia) || referencia.Trim().Length > ReferenciaMaxLength)
            throw new BusinessRuleException("SALIDA_REFERENCIA_INVALIDA",
                $"La referencia de la salida es obligatoria (máx. {ReferenciaMaxLength}).");
        if (lineas.Count == 0)
            throw new BusinessRuleException("SALIDA_SIN_LINEAS", "La salida debe tener al menos una línea.");
        if (lineas.Select(l => l.Posicion).Distinct().Count() != lineas.Count)
            throw new BusinessRuleException("SALIDA_POSICION_REPETIDA", "Cada posición del pedido va una sola vez en la salida.");

        var salida = new SalidaFacturable(Guid.CreateVersion7(), pedido.EmpresaId, pedido.Id,
            previas.Count == 0 ? 1 : previas.Max(s => s.NumeroParcialidad) + 1, origen, referencia.Trim(), ahora);

        var previasPorPosicion = previas.SelectMany(s => s.Lineas).GroupBy(l => l.Posicion)
            .ToDictionary(g => g.Key, g => (Cantidad: g.Sum(l => l.Cantidad), Importe: g.Sum(l => l.Importe)));

        foreach (var (posicion, cantidad) in lineas.OrderBy(l => l.Posicion))
        {
            var origenLinea = pedido.Lineas.FirstOrDefault(l => l.Posicion == posicion)
                ?? throw new BusinessRuleException("SALIDA_POSICION_INEXISTENTE", $"El pedido no tiene la posición {posicion}.");
            if (cantidad <= 0)
                throw new BusinessRuleException("SALIDA_CANTIDAD_INVALIDA", $"La cantidad de la posición {posicion} debe ser mayor a cero.");

            var anterior = previasPorPosicion.GetValueOrDefault(posicion);
            var pendiente = origenLinea.Cantidad - anterior.Cantidad;
            if (cantidad > pendiente)
                throw new BusinessRuleException("SALIDA_EXCEDE_PEDIDO",
                    $"La posición {posicion} tiene {pendiente:0.####} pendientes y la salida trae {cantidad:0.####}.");

            var importe = cantidad == pendiente
                ? origenLinea.Importe - anterior.Importe
                : Math.Round(cantidad * origenLinea.Precio - origenLinea.Descuento * cantidad / origenLinea.Cantidad,
                    2, MidpointRounding.AwayFromZero);
            salida._lineas.Add(new SalidaFacturableLinea(Guid.CreateVersion7(), salida.Id, posicion, cantidad, importe));
        }

        salida.Total = salida._lineas.Sum(l => l.Importe);
        return salida;
    }

    /// <summary>Misma salida reenviada: mismas posiciones y cantidades.</summary>
    public bool MismasCantidades(IReadOnlyList<(int Posicion, decimal Cantidad)> lineas) =>
        lineas.Count == _lineas.Count
        && lineas.All(l => _lineas.Any(x => x.Posicion == l.Posicion && x.Cantidad == l.Cantidad));
}

/// <summary>Posición del pedido y cantidad que sale en la parcialidad.</summary>
public sealed class SalidaFacturableLinea : BaseEntity, IBelongsToAggregate
{
    public Guid SalidaFacturableId { get; private set; }
    public int Posicion { get; private set; }
    public decimal Cantidad { get; private set; }
    public decimal Importe { get; private set; }

    public Guid AggregateRootId => SalidaFacturableId;

    private SalidaFacturableLinea() { }

    internal SalidaFacturableLinea(Guid id, Guid salidaId, int posicion, decimal cantidad, decimal importe) : base(id)
    {
        SalidaFacturableId = salidaId;
        Posicion = posicion;
        Cantidad = cantidad;
        Importe = importe;
    }
}
