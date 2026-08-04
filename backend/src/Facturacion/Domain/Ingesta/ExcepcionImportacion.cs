using Millet.Facturacion.Domain.Pedidos;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Ingesta;

/// <summary>
/// Entrada en la bandeja de excepciones de importación (§12.1 diseño): un
/// registro del origen que no pudo convertirse en <c>PedidoFacturable</c> (o una
/// operación sobre uno ya facturado que requiere revisión manual). No se pierde;
/// un operador la resuelve.
/// </summary>
public sealed class ExcepcionImportacion : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public OrigenPedido Origen { get; private set; }

    /// <summary>Referencia del pedido en el origen (clave natural).</summary>
    public string PedidoRef { get; private set; } = string.Empty;

    public MotivoExcepcion Motivo { get; private set; }
    public string? Detalle { get; private set; }

    public bool Resuelto { get; private set; }
    public Guid? ResueltoPor { get; private set; }
    public DateTimeOffset? ResueltoAt { get; private set; }

    private ExcepcionImportacion() { }

    private ExcepcionImportacion(Guid id, Guid empresaId, OrigenPedido origen, string pedidoRef, MotivoExcepcion motivo, string? detalle) : base(id)
    {
        if (string.IsNullOrWhiteSpace(pedidoRef))
            throw new BusinessRuleException("EXCEPCION_PEDIDO_REF_INVALIDA", "La referencia del pedido es obligatoria.");

        EmpresaId = empresaId;
        Origen = origen;
        PedidoRef = pedidoRef;
        Motivo = motivo;
        Detalle = detalle;
        Resuelto = false;
    }

    public static ExcepcionImportacion Crear(Guid empresaId, OrigenPedido origen, string pedidoRef, MotivoExcepcion motivo, string? detalle) =>
        new(Guid.CreateVersion7(), empresaId, origen, pedidoRef, motivo, detalle);

    /// <summary>
    /// Refresca motivo/detalle cuando la causa se re-observa en un tick
    /// posterior de la ingesta (upsert de bandeja, FAC-ING-PR3): una sola
    /// entrada abierta por pedido, no una por reintento.
    /// </summary>
    public void Actualizar(MotivoExcepcion motivo, string? detalle)
    {
        if (Resuelto)
            throw new BusinessRuleException("EXCEPCION_YA_RESUELTA", "La excepción ya está resuelta.");

        Motivo = motivo;
        Detalle = detalle;
    }

    public void Resolver(Guid? usuarioId, DateTimeOffset ahora)
    {
        if (Resuelto)
            throw new BusinessRuleException("EXCEPCION_YA_RESUELTA", "La excepción ya está resuelta.");

        Resuelto = true;
        ResueltoPor = usuarioId;
        ResueltoAt = ahora;
    }
}
