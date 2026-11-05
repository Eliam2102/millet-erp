using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.revaluacion.registrada.v1</c> (CXP-12): uno por proveedor y sucursal con diferencia al cerrar el mes.
/// Contabilidad lo contabiliza con fecha del último día del mes: pérdida = cargo a resultados y abono al proveedor;
/// utilidad = cargo al proveedor y abono a resultados. <paramref name="Perdida"/> y <paramref name="Utilidad"/> son netas por proveedor
/// (solo una de las dos trae valor). El motor contable exige la sucursal en toda póliza automática.
/// </summary>
public sealed record RevaluacionCierreRegistradaIntegrationEvent(
    Guid EmpresaId, DateTimeOffset OcurridoEn, Guid RevaluacionId, Guid ProveedorId, Guid SucursalId, DateOnly FechaCierre, decimal Perdida, decimal Utilidad)
    : IntegrationEvent("cuentas_por_pagar.revaluacion.registrada.v1", EmpresaId, OcurridoEn);

/// <summary>
/// EventType <c>cuentas_por_pagar.revaluacion.revertida.v1</c> (CXP-12): la reversa automática del día 1 del periodo siguiente.
/// Mismos importes que el evento original; Contabilidad los asienta con los lados contrarios y fecha <paramref name="FechaReversa"/>.
/// </summary>
public sealed record RevaluacionCierreRevertidaIntegrationEvent(
    Guid EmpresaId, DateTimeOffset OcurridoEn, Guid RevaluacionId, Guid ProveedorId, Guid SucursalId, DateOnly FechaReversa, decimal Perdida, decimal Utilidad)
    : IntegrationEvent("cuentas_por_pagar.revaluacion.revertida.v1", EmpresaId, OcurridoEn);
