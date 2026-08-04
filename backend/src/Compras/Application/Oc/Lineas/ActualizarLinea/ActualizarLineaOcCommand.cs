using MediatR;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Application.Oc.Lineas.ActualizarLinea;

/// <summary>
/// PATCH parcial sobre una línea de OC. Permitido en
/// <c>Borrador</c>/<c>Rechazada</c> y solo si la línea aún no tiene
/// recepción/facturación. Nullables = no tocar; flags <c>limpiarX</c>
/// setean a null.
/// </summary>
public sealed record ActualizarLineaOcCommand(
    Guid OrdenCompraId,
    Guid LineaId,
    Guid? ArticuloId = null,
    decimal? Cantidad = null,
    string? UnidadMedida = null,
    decimal? PrecioUnitario = null,
    DescuentoTipo? DescuentoTipo = null,
    decimal? DescuentoValor = null,
    string? IndicadorImpuestos = null,
    Guid? DepartamentoSolicitanteId = null,
    string? DescripcionExtendida = null,
    DateTimeOffset? FechaEntregaLinea = null,
    bool LimpiarDescripcionExtendida = false,
    bool LimpiarFechaEntregaLinea = false,
    // Fase E PR3: solo aplica a líneas MANUALES. En heredadas el dominio lo
    // rechaza con LINEA_OC_CC_HEREDADO_INMUTABLE (ADR-0050). null = no tocar.
    // PR3.1: sigue siendo "no tocar" (el PATCH parcial no se rompe), pero el
    // dominio valida el POST-ESTADO: si la línea es manual y queda sin CC,
    // rechaza con LINEA_OC_MANUAL_CENTRO_COSTO_REQUERIDO.
    Guid? CentroCostoId = null) : IRequest;
