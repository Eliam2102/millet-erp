using Millet.SharedKernel.Application.Integration;
namespace Millet.Tesoreria.Application.Integration;
public sealed record AnticipoAgenteDevolucionConfirmadaEvent(Guid Empresa, DateTimeOffset FechaEvento, Guid AnticipoAgenteId,
    Guid DevolucionId, decimal Monto, string? ReferenciaBancaria, DateOnly FechaValor, Guid SucursalId,
    Guid ProveedorId, Guid CuentaBancariaId, Guid MovimientoId)
    : IntegrationEvent("tesoreria.anticipo-agente.devolucion-confirmada.v1", Empresa, FechaEvento);
