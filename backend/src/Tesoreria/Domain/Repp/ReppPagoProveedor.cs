using Millet.SharedKernel.Domain;
namespace Millet.Tesoreria.Domain.Repp;
public sealed class ReppPagoProveedor : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid ReppId { get; private set; }
    public Guid PagoId { get; private set; }
    public decimal Importe { get; private set; }
    private ReppPagoProveedor() { }
    public ReppPagoProveedor(Guid empresaId, Guid reppId, Guid pagoId, decimal importe) : base(Guid.CreateVersion7())
    { EmpresaId = empresaId; ReppId = reppId; PagoId = pagoId; Importe = importe; }
}
