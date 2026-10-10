using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.CuentasPorPagar.Domain.AnticipoProveedor;
public sealed class ConfiguracionAnticipoProveedor : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid ProveedorId { get; private set; }
    public string Serie { get; private set; } = "FANT";
    private ConfiguracionAnticipoProveedor() { }
    public ConfiguracionAnticipoProveedor(Guid empresaId, Guid proveedorId, string serie) : base(Guid.CreateVersion7())
    { EmpresaId = empresaId; ProveedorId = proveedorId; CambiarSerie(serie); }
    public void CambiarSerie(string serie)
    {
        if (string.IsNullOrWhiteSpace(serie) || serie.Trim().Length > 25)
            throw new BusinessRuleException("ANTICIPO_SERIE_INVALIDA", "La serie de anticipos es obligatoria y admite hasta 25 caracteres.");
        Serie = serie.Trim().ToUpperInvariant();
    }
}
