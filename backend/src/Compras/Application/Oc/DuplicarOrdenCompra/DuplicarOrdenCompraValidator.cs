using FluentValidation;
using Millet.Compras.Domain;

namespace Millet.Compras.Application.Oc.DuplicarOrdenCompra;

public sealed class DuplicarOrdenCompraValidator : AbstractValidator<DuplicarOrdenCompraCommand>
{
    public DuplicarOrdenCompraValidator()
    {
        RuleFor(c => c.OrdenCompraOrigenId).NotEmpty().WithErrorCode("OC_ORIGEN_REQUERIDA");

        RuleFor(c => c.SucursalCodigo)
            .NotEmpty().WithErrorCode("SUCURSAL_CODIGO_REQUERIDO")
            .Must(PrefijoSucursalCompras.EsValido)
            .WithErrorCode("SUCURSAL_CODIGO_FORMATO_INVALIDO")
            .WithMessage("El código de sucursal debe ser una clave del 101 al 105 o de 2 a 4 letras mayúsculas.");

        RuleFor(c => c.FolioAnio).InclusiveBetween((short)2000, (short)2100)
            .WithErrorCode("FOLIO_ANIO_FUERA_DE_RANGO");
    }
}
