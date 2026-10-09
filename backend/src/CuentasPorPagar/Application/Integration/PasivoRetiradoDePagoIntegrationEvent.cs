using Millet.SharedKernel.Application.Integration;
namespace Millet.CuentasPorPagar.Application.Integration;
public sealed record PasivoRetiradoDePagoIntegrationEvent(Guid EmpresaId, DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId, Guid ProveedorId, string Moneda, string Motivo)
    : IntegrationEvent("cuentas_por_pagar.pasivo.retirado-de-pago.v1", EmpresaId, OcurridoEn);
