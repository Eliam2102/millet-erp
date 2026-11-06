using Millet.SharedKernel.Application.Integration;
namespace Millet.ComercioExterior.Application;
public sealed record GastoAnticipoAplicado(Guid GastoId, Guid FacturaProveedorId, decimal Importe);
public sealed record AnticipoGastosAplicadosEvent(Guid Empresa, DateTimeOffset Fecha, Guid AnticipoAgenteId,
    Guid AgenteId, string FolioExpediente, int Revision, IReadOnlyList<GastoAnticipoAplicado> Gastos)
    : IntegrationEvent("comercio_exterior.anticipo-agente.gastos-aplicados.v1", Empresa, Fecha);
