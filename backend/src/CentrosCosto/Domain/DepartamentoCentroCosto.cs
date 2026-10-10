using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CentrosCosto.Domain;

/// <summary>Equivalencia organizacional ADM08. Identificadores externos lógicos, sin FK entre módulos.</summary>
public sealed class DepartamentoCentroCosto : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; private set; }
    Guid IPerteneceAEmpresa.EmpresaId { get => EmpresaId; set => EmpresaId = value; }
    public Guid SucursalId { get; private set; }
    public Guid DepartamentoId { get; private set; }
    public Guid CentroCostoId { get; private set; }
    public string Observaciones { get; private set; } = string.Empty;
    private DepartamentoCentroCosto() { }
    public DepartamentoCentroCosto(Guid id, Guid empresaId, Guid sucursalId, Guid departamentoId,
        Guid centroCostoId, string observaciones) : base(id)
    {
        if (empresaId == Guid.Empty || sucursalId == Guid.Empty || departamentoId == Guid.Empty)
            throw new BusinessRuleException("CECO_EQUIVALENCIA_INVALIDA", "Selecciona empresa, sucursal y departamento.");
        EmpresaId = empresaId; SucursalId = sucursalId; DepartamentoId = departamentoId;
        Cambiar(centroCostoId, observaciones);
    }
    public void Cambiar(Guid centroCostoId, string observaciones)
    {
        if (centroCostoId == Guid.Empty || string.IsNullOrWhiteSpace(observaciones) || observaciones.Length > 500)
            throw new BusinessRuleException("CECO_EQUIVALENCIA_INVALIDA", "Selecciona un centro de costo e indica el motivo (máximo 500 caracteres).");
        CentroCostoId = centroCostoId; Observaciones = observaciones.Trim();
    }
}
