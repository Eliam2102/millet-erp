using FluentValidation;

namespace Millet.Compras.Application.Oc.ActualizarContactoProveedor;

public sealed class ActualizarContactoProveedorValidator
    : AbstractValidator<ActualizarContactoProveedorCommand>
{
    public ActualizarContactoProveedorValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty().WithErrorCode("OC_ID_REQUERIDA");
        RuleFor(c => c.Nombre).MaximumLength(200).WithErrorCode("NOMBRE_DEMASIADO_LARGO")
            .When(c => c.Nombre is not null);
        RuleFor(c => c.Email).MaximumLength(200).WithErrorCode("EMAIL_DEMASIADO_LARGO")
            .When(c => c.Email is not null);
        RuleFor(c => c.Telefono).MaximumLength(50).WithErrorCode("TELEFONO_DEMASIADO_LARGO")
            .When(c => c.Telefono is not null);
    }
}
