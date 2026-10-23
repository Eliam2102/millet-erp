using Millet.SharedKernel.Domain;
namespace Millet.Almacen.Domain.Saldos;
/// <summary>Movimiento de valor, separado de las líneas físicas cuyo CHECK exige cantidad positiva.</summary>
public sealed class AjusteCostoExpediente : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public int NumeroVersion { get; private set; }
    public Guid PartidaId { get; private set; }
    public Guid UbicacionId { get; private set; }
    public Guid ArticuloId { get; private set; }
    public decimal AcumuladoMxn { get; private set; }
    public decimal DiferenciaMxn { get; private set; }
    public decimal InventarioMxn { get; private set; }
    public decimal CostoVentasMxn { get; private set; }
    public DateTimeOffset Fecha { get; private set; }
    private AjusteCostoExpediente() { }
    public AjusteCostoExpediente(Guid empresa, Guid expediente, int version, Guid partida, Guid ubicacion, Guid articulo,
        decimal acumulado, decimal diferencia, decimal inventario, DateTimeOffset fecha) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresa; ExpedienteId = expediente; NumeroVersion = version; PartidaId = partida;
        UbicacionId = ubicacion; ArticuloId = articulo; AcumuladoMxn = acumulado; DiferenciaMxn = diferencia;
        InventarioMxn = inventario; CostoVentasMxn = diferencia - inventario; Fecha = fecha;
    }
}
