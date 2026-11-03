import type { ProveedorPac } from '@/features/integraciones-fiscal/api/types';

/**
 * Query keys del módulo Integraciones.Fiscal. Mismo shape que los demás
 * módulos: <c>['integraciones-fiscal', recurso, acción, ...filtros]</c>.
 */
export const integracionesFiscalKeys = {
  all: ['integraciones-fiscal'] as const,

  configuracion: () => [...integracionesFiscalKeys.all, 'configuracion'] as const,
  configuracionByEmpresa: (empresaId: string, proveedor: ProveedorPac) =>
    [...integracionesFiscalKeys.configuracion(), empresaId, proveedor] as const,

  // F6.9: estado del CSD (banner) e historial de certificados.
  csdEstado: () => [...integracionesFiscalKeys.all, 'csd-estado'] as const,
  certificados: (empresaId: string, proveedor: ProveedorPac) =>
    [...integracionesFiscalKeys.configuracionByEmpresa(empresaId, proveedor), 'certificados'] as const,

  rfcsReceptores: () => [...integracionesFiscalKeys.all, 'rfcs-receptores'] as const,
  rfcsReceptoresByEmpresa: (empresaId: string) =>
    [...integracionesFiscalKeys.rfcsReceptores(), empresaId] as const,
};
