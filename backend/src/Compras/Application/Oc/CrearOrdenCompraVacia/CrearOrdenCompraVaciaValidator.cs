using System.Text.RegularExpressions;
using FluentValidation;

namespace Millet.Compras.Application.Oc.CrearOrdenCompraVacia;

/// <summary>
/// Validación de input shape de <see cref="CrearOrdenCompraVaciaCommand"/>.
/// Errores devuelven HTTP 400 (ADR-0010). Las invariantes del agregado
/// <c>OrdenCompra</c> actúan como red de seguridad y devuelven 422 si el
/// validator dejó pasar algo que el dominio rechaza.
///
/// Notar: <c>EmpresaId</c> y <c>CompradorTitularId</c> NO están en el
/// command; el handler los resuelve desde el JWT. <c>EncargadoComprasId</c>
/// es opcional; el validator solo verifica que si viene, no sea
/// <c>Guid.Empty</c>.
/// </summary>
public sealed partial class CrearOrdenCompraVaciaValidator : AbstractValidator<CrearOrdenCompraVaciaCommand>
{
    private const string SucursalCodigoPattern = @"^[A-Z]{2,4}$";

    [GeneratedRegex(SucursalCodigoPattern, RegexOptions.CultureInvariant)]
    private static partial Regex SucursalCodigoRegex();

    public CrearOrdenCompraVaciaValidator()
    {
        RuleFor(c => c.SucursalDestinoId).NotEmpty().WithErrorCode("SUCURSAL_REQUERIDA");
        RuleFor(c => c.ProveedorId).NotEmpty().WithErrorCode("PROVEEDOR_REQUERIDO");
        RuleFor(c => c.CondicionesPagoId).NotEmpty().WithErrorCode("CONDICIONES_PAGO_REQUERIDAS");
        RuleFor(c => c.UsoPrincipalId).NotEmpty().WithErrorCode("USO_PRINCIPAL_REQUERIDO");

        RuleFor(c => c.EncargadoComprasId)
            .NotEqual(Guid.Empty).WithErrorCode("ENCARGADO_INVALIDO")
            .When(c => c.EncargadoComprasId.HasValue);

        RuleFor(c => c.SucursalCodigo)
            .NotEmpty().WithErrorCode("SUCURSAL_CODIGO_REQUERIDO")
            .Must(c => SucursalCodigoRegex().IsMatch(c))
            .WithErrorCode("SUCURSAL_CODIGO_FORMATO_INVALIDO")
            .WithMessage("El código de sucursal debe ser 2-4 letras mayúsculas (ej. MID, MX, CAN).");

        RuleFor(c => c.FolioAnio)
            .InclusiveBetween((short)2000, (short)2100)
            .WithErrorCode("FOLIO_ANIO_FUERA_DE_RANGO");

        RuleFor(c => c.Moneda)
            .NotEmpty().WithErrorCode("MONEDA_REQUERIDA")
            .Length(3).WithErrorCode("MONEDA_FORMATO_INVALIDO")
            .WithMessage("La moneda debe ser un código ISO 4217 de 3 caracteres (ej. MXN, USD).");

        // Coherencia moneda/tipo de cambio: el validador no fija si la
        // OC es MXN o no — solo verifica formato. La invariante (TipoCambio
        // requerido si Moneda != MXN; null si Moneda == MXN) la aplica el
        // agregado y devuelve 422.
        RuleFor(c => c.TipoCambio)
            .GreaterThan(0).WithErrorCode("TIPO_CAMBIO_INVALIDO")
            .When(c => c.TipoCambio.HasValue);

        RuleFor(c => c.Observaciones)
            .MaximumLength(1000).WithErrorCode("OBSERVACIONES_DEMASIADO_LARGAS")
            .When(c => c.Observaciones is not null);

        RuleFor(c => c.MotivoSinRequisicion)
            .NotEmpty().WithErrorCode("MOTIVO_SIN_RQ_REQUERIDO")
            .When(c => c.SinRequisicionPrevia);

        RuleFor(c => c.MotivoSinRequisicion)
            .MaximumLength(500).WithErrorCode("MOTIVO_SIN_RQ_DEMASIADO_LARGO")
            .When(c => c.MotivoSinRequisicion is not null);

        RuleFor(c => c.OcOrigenId)
            .NotEqual(Guid.Empty).WithErrorCode("OC_ORIGEN_INVALIDA")
            .When(c => c.OcOrigenId.HasValue);
    }
}
