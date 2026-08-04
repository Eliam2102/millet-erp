using System.Text.RegularExpressions;
using FluentValidation;

namespace Millet.Compras.Application.Oc.DuplicarOrdenCompra;

public sealed partial class DuplicarOrdenCompraValidator : AbstractValidator<DuplicarOrdenCompraCommand>
{
    private const string SucursalCodigoPattern = @"^[A-Z]{2,4}$";

    [GeneratedRegex(SucursalCodigoPattern, RegexOptions.CultureInvariant)]
    private static partial Regex SucursalCodigoRegex();

    public DuplicarOrdenCompraValidator()
    {
        RuleFor(c => c.OrdenCompraOrigenId).NotEmpty().WithErrorCode("OC_ORIGEN_REQUERIDA");

        RuleFor(c => c.SucursalCodigo)
            .NotEmpty().WithErrorCode("SUCURSAL_CODIGO_REQUERIDO")
            .Must(c => SucursalCodigoRegex().IsMatch(c))
            .WithErrorCode("SUCURSAL_CODIGO_FORMATO_INVALIDO")
            .WithMessage("El código de sucursal debe ser 2-4 letras mayúsculas.");

        RuleFor(c => c.FolioAnio).InclusiveBetween((short)2000, (short)2100)
            .WithErrorCode("FOLIO_ANIO_FUERA_DE_RANGO");
    }
}
