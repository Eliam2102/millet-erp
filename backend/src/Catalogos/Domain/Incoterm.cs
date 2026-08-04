using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Catalogos.Domain;

/// <summary>
/// Incoterm del catálogo cross-empresa <c>compartido.incoterms</c>
/// (F9-PR1). Reglas internacionales de comercio (Incoterms 2020) que
/// definen responsabilidades de envío, seguro y aranceles entre
/// comprador y vendedor. Usado por OC en importaciones (§4.7 del
/// 01-diseño OC).
/// </summary>
public sealed class Incoterm : BaseEntity, IAuditable
{
    /// <summary>Código ISO de 3 letras (EXW, FCA, CPT, CIP, DAP, DPU, DDP, FAS, FOB, CFR, CIF).</summary>
    public string Codigo { get; private set; } = string.Empty;

    /// <summary>Nombre descriptivo en español.</summary>
    public string Nombre { get; private set; } = string.Empty;

    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private Incoterm() { }

    public Incoterm(
        Guid id,
        string codigo,
        string nombre,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length is < 2 or > 4)
            throw new BusinessRuleException("INCOTERM_CODIGO_INVALIDO",
                "El código debe tener 2-4 caracteres.");
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 100)
            throw new BusinessRuleException("INCOTERM_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 100 caracteres.");

        Codigo = codigo.ToUpperInvariant();
        Nombre = nombre;
        Estatus = estatus;
    }

    /// <summary>
    /// PATCH parcial (F-Admin-PR5.2). Inmutable: <see cref="Codigo"/>
    /// (business key).
    /// </summary>
    public void ActualizarDatos(string? nombre = null)
    {
        if (nombre is not null)
        {
            if (nombre.Length is 0 or > 100)
                throw new BusinessRuleException("INCOTERM_NOMBRE_INVALIDO",
                    "El nombre es requerido y no puede exceder 100 caracteres.");
            Nombre = nombre;
        }
    }

    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;
}
