using MediatR;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Application.Oc.ActualizarCabecera;

/// <summary>
/// PATCH parcial sobre la cabecera de la OC en estado
/// <c>Borrador</c>/<c>Rechazada</c>. Cualquier campo nullable que llegue
/// como <c>null</c> NO se modifica; los flags <c>limpiarX</c> setean a
/// null. Inmutables (requieren Cancelar + Duplicar): EmpresaId, Folio,
/// SucursalDestinoId, CompradorTitularId, SinRequisicionPrevia.
/// </summary>
public sealed record ActualizarCabeceraOcCommand(
    Guid OrdenCompraId,
    Guid? ProveedorId,
    Guid? CondicionesPagoId,
    Guid? UsoPrincipalId,
    Guid? EncargadoComprasId,
    string? Moneda,
    decimal? TipoCambio,
    bool? EsImportacion,
    bool? CotizacionExcepcionada,
    string? Observaciones,
    DateOnly? FechaEntregaEsperada,
    DescuentoTipo? DescuentoGlobalTipo,
    decimal? DescuentoGlobalValor,
    decimal? GastosAdicionales,
    decimal? Redondeo,
    bool LimpiarObservaciones = false,
    bool LimpiarFechaEntregaEsperada = false,
    bool LimpiarDescuentoGlobal = false,
    bool LimpiarTipoCambio = false) : IRequest;
