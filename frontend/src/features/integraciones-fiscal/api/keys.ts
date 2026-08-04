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

  rfcsReceptores: () => [...integracionesFiscalKeys.all, 'rfcs-receptores'] as const,
  rfcsReceptoresByEmpresa: (empresaId: string) =>
    [...integracionesFiscalKeys.rfcsReceptores(), empresaId] as const,
};
