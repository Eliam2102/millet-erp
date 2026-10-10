using Millet.CentrosCosto.Application.PublicPorts;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.CentrosCosto.Application.Departamentos;

public static class CentroCostoCapturaRegla
{
    public const string SinEquivalencia = "Tu departamento no tiene centro de costo asignado; pídelo a Contabilidad";
    public static Guid Resolver(CentroCostoCaptura contexto, Guid? elegido)
    {
        if (!contexto.PuedeElegir && contexto.Heredado is null)
            throw new BusinessRuleException("CECO_DEPARTAMENTO_SIN_EQUIVALENCIA", SinEquivalencia);
        var id = elegido ?? contexto.Heredado?.Id;
        if (id is null)
            throw new BusinessRuleException("CECO_DEPARTAMENTO_SIN_EQUIVALENCIA", SinEquivalencia);
        if (!contexto.PuedeElegir && id != contexto.Heredado?.Id)
            throw new BusinessRuleException("CECO_SOLO_LECTURA", "El centro de costo se hereda de tu departamento y es de solo lectura.");
        return id.Value;
    }
}
