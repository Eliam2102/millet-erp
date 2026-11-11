using Millet.SharedKernel.Application.Integration;
namespace Millet.ComercioExterior.Application;

/// <summary>
/// M-327: el Contador General autorizó revertir la comprobación de un anticipo al agente aduanal. Cuentas por pagar deja
/// <see cref="SaldoAFavorMxn"/> como saldo a favor del agente (amortizable contra sus facturas) y publica la póliza de reversa:
/// cargo al saldo del agente (anticipo a proveedores) y abono a Anticipo al agente aduanal; además repone a Proveedores los gastos
/// que la comprobación había aplicado (<see cref="GastosLiberadosMxn"/>). Los gastos se liberan con
/// <see cref="AnticipoGastosAplicadosEvent"/> (nueva revisión sin gastos). Llave: <see cref="SolicitudId"/>.
/// </summary>
public sealed record AnticipoComprobacionRevertidaEvent(Guid Empresa, DateTimeOffset Ocurrido, Guid ExpedienteId, string FolioExpediente,
    Guid SucursalId, Guid AnticipoAgenteId, Guid SolicitudId, Guid AgenteId, decimal SaldoAFavorMxn, decimal GastosLiberadosMxn,
    DateOnly Fecha, string Motivo, Guid SolicitadoPor, Guid AutorizadoPor)
    : IntegrationEvent("comercio_exterior.anticipo-agente.comprobacion-revertida.v1", Empresa, Ocurrido);
