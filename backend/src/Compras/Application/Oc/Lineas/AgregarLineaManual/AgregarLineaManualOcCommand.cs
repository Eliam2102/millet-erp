using MediatR;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Application.Oc.Lineas.AgregarLineaManual;

/// <summary>
/// Agrega una línea manual (sin FK a RQ) a una OC en
/// <c>Borrador</c>/<c>Rechazada</c>. Requiere <c>SinRequisicionPrevia = true</c>
/// en la cabecera (§4.3 del mapa funcional). El flujo "agregar desde RQ"
/// entra en F4-PR2.
/// </summary>
public sealed record AgregarLineaManualOcCommand(
    Guid OrdenCompraId,
    Guid ArticuloId,
    decimal Cantidad,
    string UnidadMedida,
    decimal PrecioUnitario,
    Guid DepartamentoSolicitanteId,
    DescuentoTipo? DescuentoTipo = null,
    decimal? DescuentoValor = null,
    string? IndicadorImpuestos = null,
    string? DescripcionExtendida = null,
    DateTimeOffset? FechaEntregaLinea = null,
    string? TextoAdicional = null,
    // Fase E PR3: CC-Máquina elegido por el comprador (proxy). REQUERIDO desde
    // PR3.1 — el validador lo exige (LINEA_OC_MANUAL_CENTRO_COSTO_REQUERIDO).
    // Sigue siendo Guid? en la firma para que el 400 lo dé el validador con
    // código de error, no un fallo de deserialización.
    Guid? CentroCostoId = null) : IRequest<AgregarLineaManualOcResponse>;
