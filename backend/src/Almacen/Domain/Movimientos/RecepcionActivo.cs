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
    /// <summary>Folio ENT consecutivo de la recepción (el mismo del movimiento en recepciones mixtas); null en filas anteriores al folio.</summary>
    public string? Folio { get; set; }
    // Anulación (espejo del 102 de SAP): la fila se conserva y deja de contar como recibido.
    public DateTimeOffset? AnuladaEn { get; private set; }
    public Guid? AnuladaPorId { get; private set; }
    public string? MotivoAnulacion { get; private set; }
    public bool EstaAnulada => AnuladaEn is not null;
    public void Anular(Guid por, string motivo, DateTimeOffset en)
    { AnuladaEn = en; AnuladaPorId = por; MotivoAnulacion = motivo; }
    public void VincularRecepcion(Guid recepcionId) => RecepcionId = recepcionId;
    private RecepcionActivo() { }
    public RecepcionActivo(Guid id, Guid empresa, Guid recepcion, Guid oc, Guid linea, decimal cantidad) : base(id)
    { EmpresaId = empresa; RecepcionId = recepcion; OrdenCompraId = oc; LineaOcId = linea; Cantidad = cantidad; }
}
