using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Catalogos.Domain;

/// <summary>
/// Catálogo de monedas aceptadas por el sistema. Subset de ISO 4217 que el
/// SAT acepta en CFDI. Vive en el esquema <c>compartido</c> (cross-empresa).
/// El <see cref="Codigo"/> es el identificador funcional (MXN, USD, EUR, ...).
/// Ver ADR-0014.
/// </summary>
public sealed class Moneda : BaseEntity, IAuditable
{
    public string Codigo { get; private set; } = string.Empty;

    public string Nombre { get; private set; } = string.Empty;

    public int Decimales { get; private set; } = 2;

    public bool Activa { get; private set; } = true;

    private Moneda() { } // EF Core

    public Moneda(Guid id, string codigo, string nombre, int decimales = 2, bool activa = true)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length != 3)
            throw new BusinessRuleException("MONEDA_CODIGO_INVALIDO",
                "El código debe tener exactamente 3 caracteres (ISO 4217).");
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 100)
            throw new BusinessRuleException("MONEDA_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 100 caracteres.");
        if (decimales is < 0 or > 6)
            throw new BusinessRuleException("MONEDA_DECIMALES_INVALIDOS",
                "Los decimales deben estar entre 0 y 6.");

        Codigo = codigo.ToUpperInvariant();
        Nombre = nombre;
        Decimales = decimales;
        Activa = activa;
    }

    /// <summary>
    /// PATCH parcial (F-Admin-PR5.1). Convención: parámetro <c>null</c>
    /// = no tocar. Inmutables: <see cref="BaseEntity.Id"/> y
    /// <see cref="Codigo"/> (business key).
    /// </summary>
    public void ActualizarDatos(string? nombre = null, int? decimales = null, bool? activa = null)
    {
        if (nombre is not null)
        {
            if (nombre.Length is 0 or > 100)
                throw new BusinessRuleException("MONEDA_NOMBRE_INVALIDO",
                    "El nombre es requerido y no puede exceder 100 caracteres.");
            Nombre = nombre;
        }
        if (decimales is int d)
        {
            if (d is < 0 or > 6)
                throw new BusinessRuleException("MONEDA_DECIMALES_INVALIDOS",
                    "Los decimales deben estar entre 0 y 6.");
            Decimales = d;
        }
        if (activa is bool a) Activa = a;
    }
}
