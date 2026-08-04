using FluentValidation;
using Microsoft.Extensions.Options;

namespace Millet.Integraciones.Aw.Application.Commands.RegistrarCotizacionEdi;

/// <summary>
/// Validación de input del comando. Reglas mínimas defensivas — la
/// validación a fondo del formato EDI la hace el drop service on-prem y
/// A+W al procesar. El ERP aquí solo evita basura obvia.
///
/// <para>
/// <b>RFC laxo:</b> Glass Agent ya valida formato SAT del lado PHP.
/// Aquí solo guard mínimo (12-13 chars alfanuméricos del SAT, o literal
/// "EXT" para clientes sin RFC mexicano). NO validación cruzada de
/// homoclave — el ERP no es la autoridad de validación de RFC.
/// </para>
///
/// <para>
/// <b>Sucursal:</b> el código (uppercase, ej. <c>CIR</c>) debe pertenecer
/// al catálogo <c>IntegracionesAw:Sucursales</c> del appsettings. El
/// validador inyecta <see cref="IOptionsSnapshot{T}"/> para reflejar
/// cambios de config sin reiniciar (FluentValidation se registra como
/// scoped en la pipeline). Si el catálogo está vacío en runtime
/// (config rota), TODA sucursal queda inválida — fail fast es preferible
/// a aceptar valores sin sentido.
/// </para>
/// </summary>
public sealed class RegistrarCotizacionEdiValidator : AbstractValidator<RegistrarCotizacionEdiCommand>
{
    public RegistrarCotizacionEdiValidator(IOptionsSnapshot<IntegracionesAwOptions> options)
    {
        var sucursalesValidas = options.Value.Sucursales.Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        RuleFor(x => x.QuoteReference)
            .NotEmpty().WithMessage("QuoteReference es requerido.")
            .Matches(@"^Q-\d{4}-\d{5}$")
            .WithMessage("QuoteReference debe seguir el formato 'Q-YYYY-NNNNN' (ej. 'Q-2026-00451').");

        RuleFor(x => x.Sucursal)
            .NotEmpty().WithMessage("AW_SUCURSAL_REQUERIDA: Sucursal es requerida.")
            .Must(s => !string.IsNullOrWhiteSpace(s) && sucursalesValidas.Contains(s))
            .WithMessage(_ =>
                $"AW_SUCURSAL_INVALIDA: Sucursal fuera de catálogo. Válidas: " +
                $"{string.Join(", ", sucursalesValidas.OrderBy(x => x))}.");

        RuleFor(x => x.EdiContent)
            .NotEmpty().WithMessage("EdiContent es requerido.")
            .MinimumLength(100).WithMessage("EdiContent muy pequeño (<100 chars), probablemente truncado o inválido.")
            .Must(content => content.Contains("#END#", StringComparison.Ordinal))
            .WithMessage("EdiContent debe contener la marca de fin '#END#' (formato A+W).");

        RuleFor(x => x.CustomerTaxId)
            .NotEmpty().WithMessage("CustomerTaxId es requerido.")
            .Matches(@"^([A-Z0-9]{12,13}|EXT)$")
            .WithMessage(
                "CustomerTaxId debe ser un RFC mexicano (12-13 chars alfanuméricos en mayúsculas) " +
                "o literal 'EXT' para clientes sin RFC.");

        RuleFor(x => x.CustomerName)
            .NotEmpty().WithMessage("CustomerName es requerido.")
            .MaximumLength(254);

        RuleFor(x => x.Source)
            .NotEmpty()
            .Must(s => s is "glass_agent" or "interno")
            .WithMessage("Source debe ser 'glass_agent' o 'interno'.");

        RuleFor(x => x.ItemsCount)
            .GreaterThanOrEqualTo(1).WithMessage("ItemsCount debe ser >= 1.");

        RuleFor(x => x.PayloadOriginalJson)
            .NotEmpty().WithMessage("PayloadOriginalJson es requerido (snapshot del request del cliente).");
    }
}
