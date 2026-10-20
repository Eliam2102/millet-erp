using Millet.SharedKernel.Domain;
namespace Millet.ActivosFijos.Domain;
/// <summary>Snapshot de conciliación para soportar factura antes o después de recepción.</summary>
public sealed class FacturaActivo : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid FacturaId { get; set; }
    public Guid LineaFacturaId { get; set; }
    public Guid LineaOcId { get; set; }
    public decimal Cantidad { get; set; }
    public decimal Importe { get; set; }
    public string Moneda { get; set; } = "MXN";
    public decimal TipoCambio { get; set; }
    public FacturaActivo() : base(Guid.CreateVersion7()) { }
}
