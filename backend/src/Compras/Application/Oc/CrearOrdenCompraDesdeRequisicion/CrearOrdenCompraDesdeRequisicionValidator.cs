using System.Text.RegularExpressions;
using FluentValidation;

namespace Millet.Compras.Application.Oc.CrearOrdenCompraDesdeRequisicion;

public sealed partial class CrearOrdenCompraDesdeRequisicionValidator
    : AbstractValidator<CrearOrdenCompraDesdeRequisicionCommand>
{
    private const string SucursalCodigoPattern = @"^[A-Z]{2,4}$";

    [GeneratedRegex(SucursalCodigoPattern, RegexOptions.CultureInvariant)]
    private static partial Regex SucursalCodigoRegex();

    public CrearOrdenCompraDesdeRequisicionValidator()
    {
        RuleFor(c => c.RequisicionId).NotEmpty().WithErrorCode("RQ_ID_REQUERIDA");
        RuleFor(c => c.ProveedorId).NotEmpty().WithErrorCode("PROVEEDOR_REQUERIDO");
        RuleFor(c => c.CondicionesPagoId).NotEmpty().WithErrorCode("CONDICIONES_PAGO_REQUERIDAS");
        RuleFor(c => c.UsoPrincipalId).NotEmpty().WithErrorCode("USO_PRINCIPAL_REQUERIDO");

        RuleFor(c => c.SucursalCodigo)
            .NotEmpty().WithErrorCode("SUCURSAL_CODIGO_REQUERIDO")
            .Must(c => SucursalCodigoRegex().IsMatch(c))
            .WithErrorCode("SUCURSAL_CODIGO_FORMATO_INVALIDO")
            .WithMessage("El código de sucursal debe ser 2-4 letras mayúsculas.");

        RuleFor(c => c.FolioAnio).InclusiveBetween((short)2000, (short)2100)
            .WithErrorCode("FOLIO_ANIO_FUERA_DE_RANGO");

        RuleFor(c => c.Moneda).Length(3).WithErrorCode("MONEDA_FORMATO_INVALIDO");
        RuleFor(c => c.TipoCambio).GreaterThan(0m).When(c => c.TipoCambio.HasValue)
            .WithErrorCode("TIPO_CAMBIO_INVALIDO");

        RuleFor(c => c.Observaciones).MaximumLength(1000).When(c => c.Observaciones is not null)
            .WithErrorCode("OBSERVACIONES_DEMASIADO_LARGAS");
    }
}
