using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.CartaPorte;

/// <summary>
/// Catálogo de operadores (choferes) para Carta Porte 3.1 (§4.6 levantamiento):
/// RFC, nombre y número de licencia. Catálogo del módulo Facturación.
/// </summary>
public sealed class Operador : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }

    public string Rfc { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public string NumLicencia { get; private set; } = string.Empty;

    public bool Activo { get; private set; } = true;

    private Operador() { }

    private Operador(Guid id, Guid empresaId, string rfc, string nombre, string numLicencia) : base(id)
    {
        EmpresaId = empresaId;
        Rfc = rfc.ToUpperInvariant();
        Nombre = nombre;
        NumLicencia = numLicencia;
    }

    public static Operador Crear(Guid empresaId, string rfc, string nombre, string numLicencia)
    {
        if (string.IsNullOrWhiteSpace(rfc))
            throw new BusinessRuleException("OPERADOR_RFC_INVALIDO", "El RFC del operador es obligatorio.");
        if (string.IsNullOrWhiteSpace(nombre))
            throw new BusinessRuleException("OPERADOR_NOMBRE_INVALIDO", "El nombre del operador es obligatorio.");
        if (string.IsNullOrWhiteSpace(numLicencia))
            throw new BusinessRuleException("OPERADOR_LICENCIA_INVALIDA", "El número de licencia es obligatorio.");

        return new Operador(Guid.CreateVersion7(), empresaId, rfc, nombre, numLicencia);
    }

    /// <summary>
    /// Actualiza los datos editables del operador. El RFC es inmutable —
    /// es la identidad fiscal del chofer en el catálogo.
    /// </summary>
    public void Actualizar(string nombre, string numLicencia)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new BusinessRuleException("OPERADOR_NOMBRE_INVALIDO", "El nombre del operador es obligatorio.");
        if (string.IsNullOrWhiteSpace(numLicencia))
            throw new BusinessRuleException("OPERADOR_LICENCIA_INVALIDA", "El número de licencia es obligatorio.");

        Nombre = nombre;
        NumLicencia = numLicencia;
    }

    /// <summary>Reactiva el operador. Idempotente — si ya está activo, no-op.</summary>
    public void Activar() => Activo = true;

    /// <summary>
    /// Desactiva el operador: sale de los selectores de emisión sin tocar
    /// las Cartas Porte históricas que lo referencian. Idempotente.
    /// </summary>
    public void Desactivar() => Activo = false;
}
