/**
 * Barrel del módulo Catálogos — capa de datos cross-empresa.
 */
export * from '@/features/catalogos/api/types';
export {
  useDepartamentos,
  useSucursales,
  usePuestos,
  useEmpleados,
  useAlmacenes,
  useUsuarios,
  useArticulo,
  useArticulos,
  useProveedores,
  useProveedor,
  fetchProveedoresPorRfc,
  useCondicionesPago,
  useUsosPrincipales,
  useIncoterms,
  useTransportistas,
  useRegimenesFiscales,
  useTiposDocumentoOc,
  mapById,
  esCatalogoSinPermiso,
  type ListarArticulosFiltros,
  type ListarProveedoresFiltros,
  type CondicionesPagoItem,
  type UsoPrincipalItem,
  type IncotermItem,
  type TransportistaItem,
  type RegimenFiscalItem,
  type TipoDocumentoOcItem,
} from '@/features/catalogos/api/hooks';
