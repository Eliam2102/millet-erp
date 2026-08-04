using System.Text.RegularExpressions;
using FluentValidation;

namespace Millet.Compras.Application.CrearRequisicion;

/// <summary>
/// Validación de input shape de <see cref="CrearRequisicionCommand"/>.
/// Errores devuelven HTTP 400 (ADR-0010, cuidado §13.1). Las invariantes
/// del agregado <c>Requisicion</c> actúan como red de seguridad y devuelven
/// 422 si el validator dejó pasar algo que el dominio rechaza.
///
/// Notar: <c>EmpresaId</c> y <c>CreadorId</c> NO están en el command; el
/// handler los resuelve desde el JWT. <c>RequisitanteId</c> es opcional;
/// el validator solo verifica que si viene, no sea Guid.Empty.
/// </summary>
public sealed partial class CrearRequisicionValidator : AbstractValidator<CrearRequisicionCommand>
{
    private const string SucursalCodigoPattern = @"^[A-Z]{2,4}$";

    [GeneratedRegex(SucursalCodigoPattern, RegexOptions.CultureInvariant)]
    private static partial Regex SucursalCodigoRegex();

    public CrearRequisicionValidator()
    {
        RuleFor(c => c.SucursalId).NotEmpty().WithErrorCode("SUCURSAL_REQUERIDA");
        RuleFor(c => c.DepartamentoId).NotEmpty().WithErrorCode("DEPARTAMENTO_REQUERIDO");

        RuleFor(c => c.RequisitanteId)
            .NotEqual(Guid.Empty).WithErrorCode("REQUISITANTE_INVALIDO")
            .When(c => c.RequisitanteId.HasValue);

        RuleFor(c => c.SucursalCodigo)
            .NotEmpty().WithErrorCode("SUCURSAL_CODIGO_REQUERIDO")
            .Must(c => SucursalCodigoRegex().IsMatch(c))
            .WithErrorCode("SUCURSAL_CODIGO_FORMATO_INVALIDO")
            .WithMessage("El código de sucursal debe ser 2-4 letras mayúsculas (ej. MID, MX, CAN).");

        RuleFor(c => c.FolioAnio)
            .InclusiveBetween((short)2000, (short)2100)
            .WithErrorCode("FOLIO_ANIO_FUERA_DE_RANGO");

        RuleFor(c => c.Descripcion)
            .MaximumLength(500).WithErrorCode("DESCRIPCION_DEMASIADO_LARGA")
            .When(c => c.Descripcion is not null);
    }
}
