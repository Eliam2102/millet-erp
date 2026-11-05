using Millet.SharedKernel.Domain;
namespace Millet.Almacen.Domain.Movimientos;
/// <summary>G-10: partida rechazada en el acto al recibir. No entra al inventario ni cuenta como recibida: el pendiente sigue en la OC.</summary>
public sealed class RecepcionRechazo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid RecepcionId { get; private set; }
    public Guid OrdenCompraId { get; private set; }
    public Guid LineaOcId { get; private set; }
    public Guid ArticuloId { get; private set; }
    public decimal Cantidad { get; private set; }
    public string UnidadMedida { get; private set; } = string.Empty;
    public string Motivo { get; private set; } = string.Empty;
    /// <summary>Foto o documento que respalda el rechazo; nunca sale en las lecturas (ADR-0058).</summary>
    public string EvidenciaBlobRef { get; private set; } = string.Empty;
    private RecepcionRechazo() { }
    public RecepcionRechazo(Guid id, Guid empresa, Guid recepcion, Guid oc, Guid linea, Guid articulo, decimal cantidad, string unidad, string motivo, string evidencia) : base(id)
    { EmpresaId = empresa; RecepcionId = recepcion; OrdenCompraId = oc; LineaOcId = linea; ArticuloId = articulo; Cantidad = cantidad; UnidadMedida = unidad; Motivo = motivo.Trim(); EvidenciaBlobRef = evidencia.Trim(); }
}
