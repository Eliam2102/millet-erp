using FluentValidation;

namespace Millet.Compras.Application.Oc.ActualizarCabecera;

/// <summary>
/// Validación de input shape de <see cref="ActualizarCabeceraOcCommand"/>.
/// La mayor parte de invariantes (rangos, coherencia moneda/TC) los
/// aplica el agregado y devuelve 422.
/// </summary>
public sealed class ActualizarCabeceraOcValidator : AbstractValidator<ActualizarCabeceraOcCommand>
{
    public ActualizarCabeceraOcValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty().WithErrorCode("OC_ID_REQUERIDA");

        RuleFor(c => c.ProveedorId)
            .NotEqual(Guid.Empty).WithErrorCode("PROVEEDOR_INVALIDO")
            .When(c => c.ProveedorId.HasValue);

        RuleFor(c => c.CondicionesPagoId)
            .NotEqual(Guid.Empty).WithErrorCode("CONDICIONES_PAGO_INVALIDAS")
            .When(c => c.CondicionesPagoId.HasValue);

        RuleFor(c => c.UsoPrincipalId)
            .NotEqual(Guid.Empty).WithErrorCode("USO_PRINCIPAL_INVALIDO")
            .When(c => c.UsoPrincipalId.HasValue);

        RuleFor(c => c.EncargadoComprasId)
            .NotEqual(Guid.Empty).WithErrorCode("ENCARGADO_INVALIDO")
            .When(c => c.EncargadoComprasId.HasValue);

        RuleFor(c => c.Moneda)
            .Length(3).WithErrorCode("MONEDA_FORMATO_INVALIDO")
            .When(c => c.Moneda is not null);

        RuleFor(c => c.TipoCambio)
            .GreaterThan(0m).WithErrorCode("TIPO_CAMBIO_INVALIDO")
            .When(c => c.TipoCambio.HasValue);

        RuleFor(c => c.Observaciones)
            .MaximumLength(1000).WithErrorCode("OBSERVACIONES_DEMASIADO_LARGAS")
            .When(c => c.Observaciones is not null);

        RuleFor(c => c.GastosAdicionales)
            .GreaterThanOrEqualTo(0m).WithErrorCode("GASTOS_NEGATIVOS")
            .When(c => c.GastosAdicionales.HasValue);
    }
}
