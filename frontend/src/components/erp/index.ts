/**
 * Barrel del UX kit transversal del ERP. Importar desde
 * <c>@/components/erp</c> mantiene los imports cortos en pantallas de
 * features.
 */
export { Breadcrumbs, type BreadcrumbItem, type BreadcrumbsProps } from '@/components/erp/Breadcrumbs';
export {
  EmptyState,
  type EmptyStateProps,
} from '@/components/erp/feedback/EmptyState';
export {
  ErrorState,
  type ErrorStateProps,
} from '@/components/erp/feedback/ErrorState';
export {
  TableSkeleton,
  type TableSkeletonProps,
  type TableSkeletonColumn,
} from '@/components/erp/feedback/TableSkeleton';
export {
  DomainTermTooltip,
  type DomainTermTooltipProps,
} from '@/components/erp/feedback/DomainTermTooltip';
export {
  CollaborationIndicator,
  type CollaborationIndicatorProps,
} from '@/components/erp/collaboration/CollaborationIndicator';
export {
  EditandoBanner,
  type EditandoBannerProps,
} from '@/components/erp/collaboration/EditandoBanner';
export {
  useCollaboration,
  type CollaborationPresence,
  type PresenceUser,
  type UseCollaborationOptions,
} from '@/components/erp/collaboration/useCollaboration';
export {
  ConflictResolutionDialog,
  type ConflictResolutionDialogProps,
  type ConflictDialogForm,
} from '@/components/erp/collaboration/ConflictResolutionDialog';
export { ConflictDialogProvider } from '@/components/erp/collaboration/ConflictDialogProvider';
export {
  useConflictDialog,
  type ConflictDialogApi,
  type OpenSimpleArgs,
  type OpenPreserveArgs,
} from '@/components/erp/collaboration/conflict-dialog-context';
export {
  SortableHeader,
  type SortableHeaderProps,
} from '@/components/erp/display/SortableHeader';
export {
  compareItemsBy,
  type SortDir,
  type SortState,
} from '@/components/erp/display/sort';
export { clickableRowProps } from '@/components/erp/data/clickable-row';
export {
  EstadoBadge,
  type EstadoBadgeProps,
} from '@/components/erp/display/EstadoBadge';
export {
  NivelPendienteBadge,
  type NivelPendienteBadgeProps,
} from '@/components/erp/display/NivelPendienteBadge';
export {
  NaturalezaBadge,
  type NaturalezaBadgeProps,
} from '@/components/erp/display/NaturalezaBadge';
export {
  MoneyDisplay,
  type MoneyDisplayProps,
} from '@/components/erp/display/MoneyDisplay';
export {
  DateTimeDisplay,
  type DateTimeDisplayProps,
} from '@/components/erp/display/DateTimeDisplay';

// ─── Selectors ──────────────────────────────────────────────────
export {
  ArticuloSelector,
  type ArticuloSelectorProps,
} from '@/components/erp/selectors/ArticuloSelector';
export {
  ProveedorSelector,
  type ProveedorSelectorProps,
} from '@/components/erp/selectors/ProveedorSelector';
export {
  DepartamentoSelector,
  type DepartamentoSelectorProps,
} from '@/components/erp/selectors/DepartamentoSelector';
export {
  DepartamentoSelectorPorSucursal,
  type DepartamentoSelectorPorSucursalProps,
} from '@/components/erp/selectors/DepartamentoSelectorPorSucursal';
export {
  PuestoSelector,
  type PuestoSelectorProps,
} from '@/components/erp/selectors/PuestoSelector';
export {
  EmpleadoSelector,
  type EmpleadoSelectorProps,
} from '@/components/erp/selectors/EmpleadoSelector';
export {
  SucursalSelector,
  type SucursalSelectorProps,
} from '@/components/erp/selectors/SucursalSelector';
export {
  AlmacenSelector,
  type AlmacenSelectorProps,
} from '@/components/erp/selectors/AlmacenSelector';
export {
  EntidadReordenSelector,
  type EntidadReordenSelectorProps,
} from '@/components/erp/selectors/EntidadReordenSelector';
export {
  UbicacionSelector,
  type UbicacionSelectorProps,
} from '@/components/erp/selectors/UbicacionSelector';
export {
  UsuarioSelector,
  type UsuarioSelectorProps,
} from '@/components/erp/selectors/UsuarioSelector';
export {
  OrdenCompraSelector,
  type OrdenCompraSelectorProps,
} from '@/components/erp/selectors/OrdenCompraSelector';
export {
  LineaOcSelector,
  type LineaOcSelectorProps,
} from '@/components/erp/selectors/LineaOcSelector';
export {
  RequisicionSelector,
  type RequisicionSelectorProps,
} from '@/components/erp/selectors/RequisicionSelector';
export {
  IncotermSelector,
  type IncotermSelectorProps,
} from '@/components/erp/selectors/IncotermSelector';
export {
  TransportistaSelector,
  type TransportistaSelectorProps,
} from '@/components/erp/selectors/TransportistaSelector';
export {
  CondicionesPagoSelector,
  type CondicionesPagoSelectorProps,
} from '@/components/erp/selectors/CondicionesPagoSelector';
export {
  UsoPrincipalSelector,
  type UsoPrincipalSelectorProps,
} from '@/components/erp/selectors/UsoPrincipalSelector';
export {
  RegimenFiscalSelector,
  type RegimenFiscalSelectorProps,
} from '@/components/erp/selectors/RegimenFiscalSelector';
export {
  UsoCfdiSelector,
  type UsoCfdiSelectorProps,
} from '@/components/erp/selectors/UsoCfdiSelector';
export {
  FormaPagoSelector,
  type FormaPagoSelectorProps,
} from '@/components/erp/selectors/FormaPagoSelector';
export {
  ClaveSatSelector,
  type ClaveSatSelectorProps,
} from '@/components/erp/selectors/ClaveSatSelector';

// ─── Adjuntos cross-módulo (UF3-PR2) ────────────────────────────
export {
  AdjuntosManager,
  type AdjuntosManagerProps,
  type AdjuntoItem,
} from '@/components/erp/adjuntos/AdjuntosManager';
export {
  TipoDocumentoSelector,
  type TipoDocumentoSelectorProps,
  type TipoDocumentoSelectorItem,
} from '@/components/erp/adjuntos/TipoDocumentoSelector';
export {
  AdjuntoPreview,
  type AdjuntoPreviewProps,
} from '@/components/erp/adjuntos/AdjuntoPreview';

// ─── Trazabilidad cross-módulo (UF7-PR2) ────────────────────────
export {
  ArbolDocumentos,
  type ArbolDocumentosProps,
} from '@/components/erp/trazabilidad/ArbolDocumentos';
export {
  NodoDocumento,
  type NodoDocumentoProps,
} from '@/components/erp/trazabilidad/NodoDocumento';
export {
  TipoDocumentoTrazabilidad,
  tipoDocumentoLabel,
  tipoDocumentoDetalleRoute,
  type NodoArbolDocumento,
} from '@/components/erp/trazabilidad/types';

// ─── Form fields ────────────────────────────────────────────────
export { MoneyField, type MoneyFieldProps } from '@/components/erp/forms/MoneyField';
export {
  DatePickerField,
  type DatePickerFieldProps,
} from '@/components/erp/forms/DatePickerField';
export {
  DecimalField,
  type DecimalFieldProps,
} from '@/components/erp/forms/DecimalField';
export {
  DECIMALES_FALLBACK,
  normalizarCodigoUnidad,
  cabeEnDecimales,
  stepParaDecimales,
} from '@/components/erp/forms/decimales-unidad';
export {
  useDecimalesUnidad,
  type DecimalesUnidadLookup,
} from '@/components/erp/forms/useDecimalesUnidad';
export {
  decimalesDeFila,
  filasConDecimalesInvalidos,
  evaluarDecimalesFila,
  evaluarDecimalesFilas,
  MENSAJE_DECIMALES_UNIDAD,
  MENSAJE_UNIDAD_NO_RESOLUBLE,
  type UnidadDeFila,
  type FilaCantidad,
  type EstadoDecimalesFila,
} from '@/components/erp/forms/decimales-filas';
export { AvisoUnidadNoResoluble } from '@/components/erp/forms/AvisoUnidadNoResoluble';
export {
  TextAreaField,
  type TextAreaFieldProps,
} from '@/components/erp/forms/TextAreaField';
export {
  ReporteShell,
  type ReporteShellProps,
} from '@/components/erp/reportes/ReporteShell';
