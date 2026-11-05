using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Integration;

/// <summary>
/// EventType <c>almacen.vale_regularizado.v1</c>. Un vale urgente se regularizó contra una requisición (G-3):
/// Compras descuenta lo surtido del saldo de la RQ (mismo efecto que una salida con RQ, <c>LineaRqId</c> por línea).
/// No repite la póliza: el vale ya se contabilizó cuando salió (<c>almacen.salida_requisicion.registrada.v1</c>).
/// La forma coincide con el espejo <c>SalidaRequisicionRegistradaAlmacenPayload</c> de Compras.
/// </summary>
public sealed record ValeRegularizadoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid SalidaId,
    string FolioSalida,
    Guid RqId,
    bool EsPorVale,
    IReadOnlyList<LineaSalidaPayload> Lineas)
    : IntegrationEvent("almacen.vale_regularizado.v1", EmpresaId, OcurridoEn);
