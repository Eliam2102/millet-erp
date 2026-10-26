using Millet.SharedKernel.Domain;
namespace Millet.ActivosFijos.Domain;
/// <summary>Consecutivo transaccional por empresa y clase; una carga SAP conserva su número.</summary>
public sealed class SecuenciaActivo : IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid ClaseId { get; set; }
    public int UltimoNumero { get; set; }
}
