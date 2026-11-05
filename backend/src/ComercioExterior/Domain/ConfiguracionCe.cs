using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ComercioExterior.Domain;
public sealed class ConfiguracionCe : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public int DiasHabilesIncidenciaRecepcion { get; private set; } = 2;
    private ConfiguracionCe() { }
    public ConfiguracionCe(Guid empresa) : base(Guid.CreateVersion7()) => EmpresaId = empresa;
    public void CambiarPlazo(int dias)
    {
        if (dias is < 1 or > 365) throw new BusinessRuleException("CE_PLAZO_INVALIDO", "Capture un plazo de 1 a 365 días hábiles.");
        DiasHabilesIncidenciaRecepcion = dias;
    }
}
