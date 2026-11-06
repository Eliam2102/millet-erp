using Millet.SharedKernel.Domain;
namespace Millet.CuentasPorPagar.Domain.AnticipoProveedor;
public sealed class ComprobacionAnticipoCe : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public int Revision { get; set; }
    public string? Error { get; set; }
    private ComprobacionAnticipoCe() { }
    public ComprobacionAnticipoCe(Guid id, Guid empresa) : base(id) => EmpresaId = empresa;
}
