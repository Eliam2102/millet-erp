using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Integration;
namespace Millet.ComercioExterior.Application;
public sealed record AnticipoAgenteSolicitadoEvent(Guid Empresa, DateTimeOffset FechaEvento, Guid ExpedienteId,
    string FolioExpediente, Guid SucursalId, Guid? OrdenCompraId, TipoExpediente Tipo, Guid AnticipoAgenteId,
    Guid AgenteId, decimal ImporteMxn, DateOnly Fecha, Guid SoporteCalculoId)
    : IntegrationEvent("comercio_exterior.anticipo-agente.solicitado.v1", Empresa, FechaEvento);
public sealed record AnticipoAgenteDevolucionAprobadaEvent(Guid Empresa, DateTimeOffset FechaEvento, Guid ExpedienteId,
    string FolioExpediente, Guid SucursalId, Guid AnticipoAgenteId, Guid AgenteId, Guid DevolucionId, decimal ImporteMxn)
    : IntegrationEvent("comercio_exterior.anticipo-agente.devolucion-aprobada.v1", Empresa, FechaEvento);
