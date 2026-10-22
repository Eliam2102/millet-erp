using FluentValidation;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Pedidos;

/// <summary>
/// Datos de clasificación de un pedido manual (R2 / F6.1) comunes a la captura
/// y a la edición.
/// </summary>
public interface IClasificacionPedidoManual
{
    ComportamientoFiscal ComportamientoFiscal { get; }
    ClasePedidoManual? ClaseManual { get; }
    SubtipoVentaDirecta? SubtipoVentaDirecta { get; }
    string? MotivoEmergencia { get; }
    string? ReferenciaPedidoAw { get; }
}

/// <summary>
/// Reglas de entrada (400) espejo de la validación del agregado
/// <see cref="PedidoFacturable"/>: sin clase no hay pedido manual (F6.1-b, no
/// existe clase «producción»).
/// </summary>
public sealed class ClasificacionPedidoManualValidator : AbstractValidator<IClasificacionPedidoManual>
{
    public ClasificacionPedidoManualValidator()
    {
        RuleFor(c => c.ClaseManual)
            .NotNull().WithErrorCode("PEDIDO_MANUAL_CLASE_REQUERIDA")
            .WithMessage("Indica la clase del pedido manual: venta directa o emergencia.")
            .IsInEnum();
        RuleFor(c => c.SubtipoVentaDirecta).IsInEnum();
        RuleFor(c => c.MotivoEmergencia).MaximumLength(500);
        RuleFor(c => c.ReferenciaPedidoAw).MaximumLength(50);

        When(c => c.ClaseManual == ClasePedidoManual.VentaDirecta, () =>
            RuleFor(c => c.SubtipoVentaDirecta)
                .NotNull().WithErrorCode("PEDIDO_VD_SUBTIPO_REQUERIDO")
                .WithMessage("Indica el subtipo de la venta directa: activo, almacenable o servicio."));

        When(c => c.ClaseManual == ClasePedidoManual.Emergencia, () =>
        {
            RuleFor(c => c.MotivoEmergencia)
                .NotEmpty().WithErrorCode("PEDIDO_EMERGENCIA_MOTIVO_REQUERIDO")
                .WithMessage("Indica el motivo de la captura de emergencia.");
            RuleFor(c => c.SubtipoVentaDirecta)
                .Null().WithErrorCode("PEDIDO_EMERGENCIA_SIN_SUBTIPO")
                .WithMessage("Un pedido de emergencia no lleva subtipo de venta directa.");
        });

        RuleFor(c => c.SubtipoVentaDirecta)
            .Must((c, subtipo) => (subtipo == SubtipoVentaDirecta.Activo)
                == (c.ComportamientoFiscal == ComportamientoFiscal.VentaActivoFijo))
            .When(c => c.ClaseManual is not null)
            .WithErrorCode("PEDIDO_ACTIVO_COMPORTAMIENTO")
            .WithMessage("La venta de un activo fijo va con el comportamiento fiscal «Venta de activo fijo», y ese comportamiento sólo aplica al subtipo Activo.");
    }
}

/// <summary>
/// Compuerta de permiso para la captura de emergencia (F6.1). Se evalúa en el
/// handler (no en el endpoint) porque depende del cuerpo: el endpoint exige
/// <c>facturacion.pedidos.capturar</c> y, si la clase es Emergencia, además
/// <see cref="PermisoCapturarEmergencia"/> → 403 Problem Details.
/// </summary>
public static class PermisoEmergenciaPedido
{
    /// <summary>Espejo de <c>PermisosCanonicos.FacturacionPedidosCapturarEmergencia</c> (Identidad).</summary>
    public const string PermisoCapturarEmergencia = "facturacion.pedidos.capturar-emergencia";

    public static async Task VerificarAsync(
        ClasePedidoManual? clase, ICurrentUserPermissions permisos, CancellationToken cancellationToken)
    {
        if (clase != ClasePedidoManual.Emergencia)
            return;
        if (!await permisos.TieneAsync(PermisoCapturarEmergencia, cancellationToken))
            throw new ForbiddenException(
                "PEDIDO_EMERGENCIA_SIN_PERMISO",
                "No tienes permiso para capturar pedidos de emergencia (facturacion.pedidos.capturar-emergencia).");
    }
}
