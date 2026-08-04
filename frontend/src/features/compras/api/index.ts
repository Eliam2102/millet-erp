/**
 * Barrel del módulo Compras — capa de datos. Importar desde
 * <c>@/features/compras/api</c> mantiene los imports cortos en
 * pantallas y components.
 */
export * from '@/features/compras/api/types';
export * from '@/features/compras/api/keys';
export { useRequisiciones } from '@/features/compras/api/useRequisiciones';
export { useRequisicion, useEtag } from '@/features/compras/api/useRequisicion';
export { useMotivosRechazo } from '@/features/compras/api/useMotivosRechazo';
