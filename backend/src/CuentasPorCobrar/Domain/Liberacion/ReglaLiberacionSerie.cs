using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.Liberacion;

/// <summary>
/// Catálogo de reglas de liberación por serie de folio A+W (levantamiento
/// §2 + §3.1, CXC-PR4). Es configuración, no código: las series de Millet
/// se cargan por migración (C7.2, Cuestionario 04 R2) y CxC las administra
/// desde su pantalla; cada cambio queda en la bitácora central (IAuditable).
/// Una serie sin regla evalúa crédito.
/// </summary>
public sealed class ReglaLiberacionSerie : BaseEntity, IAuditable
{
    /// <summary>Prefijo del folio del pedido A+W (match por StartsWith).</summary>
    public string Prefijo { get; private set; } = default!;

    public ComportamientoSerie Comportamiento { get; private set; }
    public bool Activo { get; private set; }

    /// <summary>Longitud máxima del prefijo (columna varchar(10)).</summary>
    public const int PrefijoMaxLength = 10;

    private ReglaLiberacionSerie() { }

    public ReglaLiberacionSerie(Guid id, string prefijo, ComportamientoSerie comportamiento, bool activo = true)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(prefijo))
            throw new BusinessRuleException("RLS_PREFIJO_VACIO", "El prefijo de serie es obligatorio.");

        var limpio = prefijo.Trim();
        if (limpio.Length > PrefijoMaxLength || !limpio.All(char.IsAsciiDigit))
            throw new BusinessRuleException("RLS_PREFIJO_INVALIDO",
                $"La serie debe tener solo dígitos y a lo más {PrefijoMaxLength} caracteres.");

        Prefijo = limpio;
        Comportamiento = ValidarComportamiento(comportamiento);
        Activo = activo;
    }

    /// <summary>C7.2: cambia lo que hace la serie; aplica a las evaluaciones siguientes.</summary>
    public void CambiarComportamiento(ComportamientoSerie comportamiento) =>
        Comportamiento = ValidarComportamiento(comportamiento);

    /// <summary>C7.2: una regla inactiva deja que la serie evalúe crédito.</summary>
    public void Activar() => Activo = true;

    public void Desactivar() => Activo = false;

    private static ComportamientoSerie ValidarComportamiento(ComportamientoSerie comportamiento) =>
        Enum.IsDefined(comportamiento)
            ? comportamiento
            : throw new BusinessRuleException("RLS_COMPORTAMIENTO_INVALIDO",
                "El comportamiento de la serie no es válido.");
}
