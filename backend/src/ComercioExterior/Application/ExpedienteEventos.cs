using Millet.SharedKernel.Application.Integration;

namespace Millet.ComercioExterior.Application;
public sealed record ExpedienteCeAbiertoEvent(Guid Empresa, DateTimeOffset Fecha, Guid ExpedienteId, string Folio) : IntegrationEvent("expediente-ce.abierto.v1", Empresa, Fecha);
public sealed record ExpedienteCeCanceladoEvent(Guid Empresa, DateTimeOffset Fecha, Guid ExpedienteId, string Motivo) : IntegrationEvent("expediente-ce.cancelado.v1", Empresa, Fecha);

public sealed record ExpedienteCeHitoRegistradoEvent(Guid Empresa, DateTimeOffset Fecha, Guid ExpedienteId, string Clave, DateOnly FechaReal, Guid ResponsableUsuarioId) : IntegrationEvent("expediente-ce.hito-registrado.v1", Empresa, Fecha);
