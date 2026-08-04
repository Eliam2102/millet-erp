using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Catalogos.Domain;

/// <summary>
/// Régimen fiscal SAT del catálogo cross-empresa
/// <c>compartido.regimenes_fiscales</c> (F9-PR1). Códigos oficiales SAT
/// (e.g., 601 General de Ley Personas Morales, 612 PFAE, 626 RESICO).
/// Usado por OC y por el motor de impuestos (F3-PR2) para resolver
/// IVA / retenciones según el par (régimen proveedor × régimen artículo).
/// </summary>
public sealed class RegimenFiscal : BaseEntity, IAuditable
{
    /// <summary>Código SAT numérico (3 dígitos).</summary>
    public string Codigo { get; private set; } = string.Empty;

    public string Nombre { get; private set; } = string.Empty;

    /// <summary>Aplica a personas físicas (true) o personas morales (false). Algunos regímenes aplican a ambos — usar la naturaleza dominante.</summary>
    public bool AplicaPersonaFisica { get; private set; }

    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private RegimenFiscal() { }

    public RegimenFiscal(
        Guid id,
        string codigo,
        string nombre,
        bool aplicaPersonaFisica,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length != 3)
            throw new BusinessRuleException("REGIMEN_FISCAL_CODIGO_INVALIDO",
                "El código SAT debe tener exactamente 3 caracteres.");
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("REGIMEN_FISCAL_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 254 caracteres.");

        Codigo = codigo;
        Nombre = nombre;
        AplicaPersonaFisica = aplicaPersonaFisica;
        Estatus = estatus;
    }
}
