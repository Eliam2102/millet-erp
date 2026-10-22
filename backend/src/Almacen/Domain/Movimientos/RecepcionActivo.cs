using Millet.SharedKernel.Domain;
namespace Millet.Almacen.Domain.Movimientos;
/// <summary>Recepción física sin inventario: conserva cantidades para el límite de la OC y el evento.</summary>
public sealed class RecepcionActivo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid RecepcionId { get; set; }
    public Guid OrdenCompraId { get; set; }
    public Guid LineaOcId { get; set; }
    public decimal Cantidad { get; set; }
    public void VincularRecepcion(Guid recepcionId) => RecepcionId = recepcionId;
    private RecepcionActivo() { }
    public RecepcionActivo(Guid id, Guid empresa, Guid recepcion, Guid oc, Guid linea, decimal cantidad) : base(id)
    { EmpresaId = empresa; RecepcionId = recepcion; OrdenCompraId = oc; LineaOcId = linea; Cantidad = cantidad; }
}
