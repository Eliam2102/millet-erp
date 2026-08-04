import { useQuery } from '@tanstack/react-query';
import { apiRequest, esApiError } from '@/lib/api';
import type {
  AlmacenListItem,
  ArticuloDetalle,
  ArticuloListItem,
  DepartamentoListItem,
  EmpleadoListItem,
  PagedCatalogoResponse,
  ProveedorDetalle,
  ProveedorListItem,
  PuestoListItem,
  SucursalListItem,
  UsuarioListItem,
} from '@/features/catalogos/api/types';
import {
  EstatusCatalogo,
  type Naturaleza,
} from '@/features/compras/api/types';

/**
 * Hooks read-only para los catálogos organizacionales y de usuarios
 * que las pantallas de Compras consumen para resolver IDs → nombres
 * en bandejas (depto/requisitante) y detalles (sucursal/almacén
 * destino).
 *
 * <para>Los catálogos son chicos (~10-50 items) y casi inmutables
 * dentro de una sesión, así que <c>staleTime: 1h</c> es razonable.
 * Si el cliente alta/edita un depto, el módulo Datos Maestros
 * invalidará explícitamente sus queries.</para>
 *
 * <para><b>Permisos</b>: <c>compartido.catalogos.leer</c> para
 * sucursales/departamentos/almacenes; <c>identidad.usuarios.leer</c>
 * para usuarios. Los hooks incluyen <c>retry: false</c> implícito vía
 * el <c>queryClient</c> default (no reintentan 4xx); el caller que
 * use el hook debe tolerar <c>error.status === 403</c> con un
 * fallback (mostrar el id raw).</para>
 */

const LIMIT_FULL = 200;

const STALE = 60 * 60 * 1000; // 1 h
const GC = 2 * 60 * 60 * 1000; // 2 h

/**
 * Filtros del listado paginado de artículos
 * (mirror del query string del endpoint).
 */
export interface ListarArticulosFiltros {
  /** Substring contra <c>clave</c>; el backend hace <c>LIKE %q%</c>. */
  clave?: string;
  /**
   * Substring contra <c>nombre</c>; el backend hace match insensible a
   * acentos y mayúsculas (folding con <c>translate</c> nativo, ADR-0045).
   * Excluyente con <c>clave</c>: si se pasan ambos, el backend prioriza
   * <c>clave</c>. El <c>&lt;ArticuloSelector/&gt;</c> garantiza que solo
   * uno viaje a la vez.
   */
  nombre?: string;
  naturaleza?: Naturaleza;
  estatus?: EstatusCatalogo;
  limit?: number;
  /** Si <c>true</c>, omite el filtro de estatus (trae activos +
   * inactivos). Útil para resolución de id → nombre en detalles
   * históricos donde el artículo puede haber sido marcado como
   * inactivo después de usarse en una OC/RQ. */
  includeInactivas?: boolean;
}

/** Filtros del listado paginado de proveedores. */
export interface ListarProveedoresFiltros {
  clave?: string;
  /**
   * Substring contra razón social o nombre comercial; el backend hace match
   * insensible a acentos y mayúsculas (folding con `translate` nativo, ADR-0045).
   * Excluyente con `clave`: si se pasan ambos, el backend prioriza `clave`. El
   * `<ProveedorSelector/>` garantiza que solo uno viaje a la vez.
   */
  nombre?: string;
  /**
   * Match exacto case-insensitive contra el RFC; filtro AND independiente
   * de `clave`/`nombre`. Lo usa el auto-resolve de proveedor desde el RFC
   * emisor del CFDI (captura de NC/anticipos en CxP).
   */
  rfc?: string;
  estatus?: EstatusCatalogo;
  limit?: number;
  /** Si <c>true</c>, omite el filtro de estatus (trae activos +
   * inactivos). Útil para resolución de id → nombre en detalles
   * históricos. */
  includeInactivas?: boolean;
}

const catalogosKeys = {
  all: ['catalogos'] as const,
  departamentos: () => [...catalogosKeys.all, 'departamentos'] as const,
  sucursales: () => [...catalogosKeys.all, 'sucursales'] as const,
  // ADM-FE-PR1: el módulo admin invalida estas familias con las claves
  // literales ['catalogos','puestos'] / ['catalogos','empleados'].
  puestos: () => [...catalogosKeys.all, 'puestos'] as const,
  empleados: () => [...catalogosKeys.all, 'empleados'] as const,
  almacenes: (sucursalId?: string) =>
    [...catalogosKeys.all, 'almacenes', sucursalId ?? 'all'] as const,
  usuarios: () => [...catalogosKeys.all, 'usuarios'] as const,
  articulos: (filtros: ListarArticulosFiltros) =>
    [...catalogosKeys.all, 'articulos', filtros] as const,
  proveedores: (filtros: ListarProveedoresFiltros) =>
    [...catalogosKeys.all, 'proveedores', filtros] as const,
  // Detalle por id (lookup puntual) — distinto de la lista filtrada de arriba.
  proveedor: (id: string) =>
    [...catalogosKeys.all, 'proveedores', 'detalle', id] as const,
  articulo: (id: string) =>
    [...catalogosKeys.all, 'articulos', 'detalle', id] as const,
  // UF2-PR1: catálogos OC consumidos por el Sheet "Nueva OC".
  // Aislados (no paginados) — los 3 son catálogos chicos seedeados
  // por F9-PR1 + UF2-PR1.
  condicionesPago: () => [...catalogosKeys.all, 'condiciones-pago'] as const,
  usosPrincipales: () => [...catalogosKeys.all, 'usos-principales'] as const,
  // UF3-PR1: catálogos OC restantes para la pestaña "Información"
  // (Logística + Importación + Financiera).
  incoterms: () => [...catalogosKeys.all, 'incoterms'] as const,
  transportistas: () => [...catalogosKeys.all, 'transportistas'] as const,
  regimenesFiscales: () =>
    [...catalogosKeys.all, 'regimenes-fiscales'] as const,
  // UF3-PR2: catálogo de tipos de documento OC para el AdjuntosManager.
  tiposDocumentoOc: () =>
    [...catalogosKeys.all, 'tipos-documento-oc'] as const,
} as const;

/** Item de <c>GET /api/v1/catalogos/condiciones-pago</c> (F9-PR1). */
export interface CondicionesPagoItem {
  id: string;
  clave: string;
  nombre: string;
  diasCredito: number;
  estatus: EstatusCatalogo;
}

/** Item de <c>GET /api/v1/catalogos/usos-principales</c> (UF2-PR1). */
export interface UsoPrincipalItem {
  id: string;
  clave: string;
  nombre: string;
  estatus: EstatusCatalogo;
}

/** Item de <c>GET /api/v1/catalogos/incoterms</c> (F9-PR1). */
export interface IncotermItem {
  id: string;
  codigo: string;
  nombre: string;
  estatus: EstatusCatalogo;
}

/** Item de <c>GET /api/v1/catalogos/transportistas</c> (F9-PR1). */
export interface TransportistaItem {
  id: string;
  clave: string;
  nombre: string;
  email: string | null;
  telefono: string | null;
  estatus: EstatusCatalogo;
}

/** Item de <c>GET /api/v1/catalogos/regimenes-fiscales</c> (F9-PR1). */
export interface RegimenFiscalItem {
  id: string;
  codigo: string;
  nombre: string;
  aplicaPersonaFisica: boolean;
  estatus: EstatusCatalogo;
}

/**
 * Item de <c>GET /api/v1/compras/catalogos/tipos-documento-oc</c>
 * (UF3-PR2). Cross-módulo: el selector genérico
 * <c>&lt;TipoDocumentoSelector/&gt;</c> acepta cualquier item con este
 * shape; CxP/Activos pueden tener su propio catálogo del mismo shape
 * y consumir el mismo selector.
 */
export interface TipoDocumentoOcItem {
  id: string;
  clave: string;
  descripcion: string;
  obligatorioSiImportacion: boolean;
  activo: boolean;
}

export function useDepartamentos() {
  return useQuery({
    queryKey: catalogosKeys.departamentos(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<
        PagedCatalogoResponse<DepartamentoListItem>
      >(`/api/v1/catalogos/departamentos?limit=${LIMIT_FULL}`, { signal });
      return data;
    },
    staleTime: STALE,
    gcTime: GC,
  });
}

export function useSucursales() {
  return useQuery({
    queryKey: catalogosKeys.sucursales(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<
        PagedCatalogoResponse<SucursalListItem>
      >(`/api/v1/catalogos/sucursales?limit=${LIMIT_FULL}`, { signal });
      return data;
    },
    staleTime: STALE,
    gcTime: GC,
  });
}

export function usePuestos() {
  return useQuery({
    queryKey: catalogosKeys.puestos(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<
        PagedCatalogoResponse<PuestoListItem>
      >(`/api/v1/catalogos/puestos?limit=${LIMIT_FULL}`, { signal });
      return data;
    },
    staleTime: STALE,
    gcTime: GC,
  });
}

export function useEmpleados() {
  return useQuery({
    queryKey: catalogosKeys.empleados(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<
        PagedCatalogoResponse<EmpleadoListItem>
      >(`/api/v1/catalogos/empleados?limit=${LIMIT_FULL}`, { signal });
      return data;
    },
    staleTime: STALE,
    gcTime: GC,
  });
}

export function useAlmacenes(sucursalId?: string) {
  return useQuery({
    queryKey: catalogosKeys.almacenes(sucursalId),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams({ limit: String(LIMIT_FULL) });
      if (sucursalId) params.set('sucursalId', sucursalId);
      const { data } = await apiRequest<
        PagedCatalogoResponse<AlmacenListItem>
      >(`/api/v1/catalogos/almacenes?${params}`, { signal });
      return data;
    },
    staleTime: STALE,
    gcTime: GC,
  });
}

export function useUsuarios() {
  return useQuery({
    queryKey: catalogosKeys.usuarios(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<PagedCatalogoResponse<UsuarioListItem>>(
        `/api/v1/identidad/usuarios?limit=${LIMIT_FULL}`,
        { signal },
      );
      return data;
    },
    staleTime: STALE,
    gcTime: GC,
  });
}

/**
 * <c>useArticulos(filtros)</c> — listado paginado de artículos cross-
 * empresa con búsqueda lazy por clave. El caller (típicamente
 * <c>&lt;ArticuloSelector/&gt;</c>) aplica debounce sobre el input
 * antes de pasar <c>clave</c> aquí; este hook NO debouncea por sí
 * solo (cada cambio de filtros = nuevo cache key).
 *
 * <para>Filtra por default <c>estatus=Activo</c> a menos que el
 * caller pida otro — los selectores de form solo deben mostrar
 * artículos activos, no inactivos ni en revisión.</para>
 *
 * <para><c>staleTime</c> bajo (30s): la búsqueda con substring debe
 * reflejar cambios casi inmediatos si el cliente alta un nuevo
 * artículo desde Datos Maestros.</para>
 */
export function useArticulos(filtros: ListarArticulosFiltros = {}) {
  return useQuery({
    queryKey: catalogosKeys.articulos(filtros),
    queryFn: async ({ signal }) => {
      const path = buildArticulosPath(filtros);
      const { data } = await apiRequest<
        PagedCatalogoResponse<ArticuloListItem>
      >(path, { signal });
      return data;
    },
    staleTime: 30_000,
    gcTime: 5 * 60_000,
  });
}

function buildArticulosPath(filtros: ListarArticulosFiltros): string {
  const params = new URLSearchParams();
  // Default: solo activos. Si el caller pasa otro estatus o pide
  // includeInactivas, ajustamos.
  if (!filtros.includeInactivas) {
    const estatus = filtros.estatus ?? EstatusCatalogo.Activo;
    params.set('estatus', String(estatus));
  }
  if (filtros.naturaleza != null)
    params.set('naturaleza', String(filtros.naturaleza));
  // clave y nombre son excluyentes; clave tiene precedencia (igual que el
  // backend). Se hace trim y se OMITE el param si queda vacío/whitespace —
  // así "limpiar" una caja (queda "") no contamina la URL ni el filtro de
  // la otra. El selector solo manda uno, pero si llegaran ambos no
  // emitimos nombre. ADR-0045.
  const claveTrim = filtros.clave?.trim();
  const nombreTrim = filtros.nombre?.trim();
  if (claveTrim) params.set('clave', claveTrim);
  else if (nombreTrim) params.set('nombre', nombreTrim);
  params.set('limit', String(filtros.limit ?? 50));
  return `/api/v1/catalogos/articulos?${params}`;
}

/**
 * <c>useProveedores(filtros)</c> — análogo a <c>useArticulos</c>:
 * búsqueda lazy por clave, default <c>estatus=Activo</c>.
 */
export function useProveedores(filtros: ListarProveedoresFiltros = {}) {
  return useQuery({
    queryKey: catalogosKeys.proveedores(filtros),
    queryFn: async ({ signal }) => {
      const path = buildProveedoresPath(filtros);
      const { data } = await apiRequest<
        PagedCatalogoResponse<ProveedorListItem>
      >(path, { signal });
      return data;
    },
    staleTime: 30_000,
    gcTime: 5 * 60_000,
  });
}

function buildProveedoresPath(filtros: ListarProveedoresFiltros): string {
  const params = new URLSearchParams();
  if (!filtros.includeInactivas) {
    const estatus = filtros.estatus ?? EstatusCatalogo.Activo;
    params.set('estatus', String(estatus));
  }
  // clave y nombre son excluyentes; clave tiene precedencia (igual que el
  // backend). Trim + se OMITE el param si queda vacío/whitespace. Espejo de
  // buildArticulosPath. ADR-0045.
  const claveTrim = filtros.clave?.trim();
  const nombreTrim = filtros.nombre?.trim();
  if (claveTrim) params.set('clave', claveTrim);
  else if (nombreTrim) params.set('nombre', nombreTrim);
  // rfc es AND independiente (match exacto server-side).
  const rfcTrim = filtros.rfc?.trim();
  if (rfcTrim) params.set('rfc', rfcTrim);
  params.set('limit', String(filtros.limit ?? 50));
  return `/api/v1/catalogos/proveedores?${params}`;
}

/**
 * Fetch imperativo de proveedores activos por RFC (match exacto
 * server-side). Para handlers de vinculación de CFDI que auto-resuelven
 * el proveedor desde el RFC emisor sin montar una query.
 */
export async function fetchProveedoresPorRfc(
  rfc: string,
): Promise<ProveedorListItem[]> {
  const path = buildProveedoresPath({ rfc, limit: 2 });
  const { data } =
    await apiRequest<PagedCatalogoResponse<ProveedorListItem>>(path);
  return data.items;
}

/**
 * <c>useProveedor(id)</c> — detalle de UN proveedor por id
 * (<c>GET /api/v1/catalogos/proveedores/{id}</c>). Resuelve la etiqueta
 * (clave + razón social) de un proveedor cuyo id viene "frío" —p. ej. un
 * filtro persistido en URL (<c>?proveedorId=</c>)— sin depender de la lista
 * capada del typeahead. Genérico y reutilizable por cualquier selector/vista
 * que tenga el id y necesite el nombre.
 *
 * <para><c>enabled</c> solo cuando hay id: con <c>null</c>/<c>undefined</c> no
 * dispara. Cache 1h (el proveedor es casi inmutable en sesión), igual que los
 * demás catálogos. 403/404 los tolera el caller (fallback al id).</para>
 */
export function useProveedor(id: string | null | undefined) {
  return useQuery({
    queryKey: catalogosKeys.proveedor(id ?? ''),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ProveedorDetalle>(
        `/api/v1/catalogos/proveedores/${id}`,
        { signal },
      );
      return data;
    },
    enabled: !!id,
    staleTime: STALE,
    gcTime: GC,
  });
}

/** Detalle de artículo por id — molde de <see cref="useProveedor"/>. */
export function useArticulo(id: string | null | undefined) {
  return useQuery({
    queryKey: catalogosKeys.articulo(id ?? ''),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ArticuloDetalle>(
        `/api/v1/catalogos/articulos/${id}`,
        { signal },
      );
      return data;
    },
    enabled: !!id,
    staleTime: STALE,
    gcTime: GC,
  });
}

/**
 * <c>useCondicionesPago()</c> — catálogo seedeado en F9-PR1 (CONTADO,
 * 15D, 30D, 45D, 60D, 90D, 120D). Cache 1h. Devuelve solo activas por
 * default; si se necesitan inactivas (admin de catálogos), pasar
 * <c>{ includeInactivas: true }</c>.
 */
export function useCondicionesPago(opts: { includeInactivas?: boolean } = {}) {
  return useQuery({
    queryKey: catalogosKeys.condicionesPago(),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (!opts.includeInactivas) {
        params.set('estatus', String(EstatusCatalogo.Activo));
      }
      const qs = params.toString();
      const path = qs
        ? `/api/v1/catalogos/condiciones-pago?${qs}`
        : '/api/v1/catalogos/condiciones-pago';
      const { data } = await apiRequest<CondicionesPagoItem[]>(path, {
        signal,
      });
      return data;
    },
    staleTime: STALE,
    gcTime: GC,
  });
}

/**
 * <c>useIncoterms()</c> — Incoterms 2020 seedeados en F9-PR1 (11
 * códigos: EXW, FCA, FAS, FOB, CFR, CIF, CPT, CIP, DAP, DPU, DDP).
 * Cache 1h. Devuelve solo activos por default. Cross-módulo (CxP los
 * consumirá).
 */
export function useIncoterms(opts: { includeInactivas?: boolean } = {}) {
  return useQuery({
    queryKey: catalogosKeys.incoterms(),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (!opts.includeInactivas) {
        params.set('estatus', String(EstatusCatalogo.Activo));
      }
      const qs = params.toString();
      const path = qs
        ? `/api/v1/catalogos/incoterms?${qs}`
        : '/api/v1/catalogos/incoterms';
      const { data } = await apiRequest<IncotermItem[]>(path, { signal });
      return data;
    },
    staleTime: STALE,
    gcTime: GC,
  });
}

/**
 * <c>useTransportistas()</c> — catálogo seedeado en F9-PR1. Cache 1h.
 * Devuelve solo activos por default. Cross-módulo (CxP los consumirá
 * para captura de fletes).
 */
export function useTransportistas(opts: { includeInactivas?: boolean } = {}) {
  return useQuery({
    queryKey: catalogosKeys.transportistas(),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (!opts.includeInactivas) {
        params.set('estatus', String(EstatusCatalogo.Activo));
      }
      const qs = params.toString();
      const path = qs
        ? `/api/v1/catalogos/transportistas?${qs}`
        : '/api/v1/catalogos/transportistas';
      const { data } = await apiRequest<TransportistaItem[]>(path, { signal });
      return data;
    },
    staleTime: STALE,
    gcTime: GC,
  });
}

/**
 * <c>useRegimenesFiscales()</c> — regímenes fiscales SAT seedeados en
 * F9-PR1 (8 códigos: 601, 603, 605, 606, 612, 621, 626, 612). Cache 1h.
 * Devuelve solo activos por default. Cross-módulo (CxP/Contabilidad
 * los consumirán para capturar régimen del proveedor/cliente).
 */
export function useRegimenesFiscales(opts: { includeInactivas?: boolean } = {}) {
  return useQuery({
    queryKey: catalogosKeys.regimenesFiscales(),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (!opts.includeInactivas) {
        params.set('estatus', String(EstatusCatalogo.Activo));
      }
      const qs = params.toString();
      const path = qs
        ? `/api/v1/catalogos/regimenes-fiscales?${qs}`
        : '/api/v1/catalogos/regimenes-fiscales';
      const { data } = await apiRequest<RegimenFiscalItem[]>(path, { signal });
      return data;
    },
    staleTime: STALE,
    gcTime: GC,
  });
}

/**
 * <c>useTiposDocumentoOc()</c> — tipos de documento adjunto del C6
 * (cotización, ficha técnica, correo autorización, pedimento, factura
 * proveedor extranjero, packing list, otro). Cache 1h. Cross-módulo:
 * el shape coincide con futuros catálogos análogos de CxP/Activos
 * para que el <c>&lt;TipoDocumentoSelector/&gt;</c> los consuma sin
 * modificación.
 *
 * <para>Permiso requerido: <c>compras.ordenes.adjuntar</c>.</para>
 */
export function useTiposDocumentoOc(
  opts: { includeInactivas?: boolean } = {},
) {
  return useQuery({
    queryKey: catalogosKeys.tiposDocumentoOc(),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (opts.includeInactivas) {
        params.set('includeInactivas', 'true');
      }
      const qs = params.toString();
      const path = qs
        ? `/api/v1/compras/catalogos/tipos-documento-oc?${qs}`
        : '/api/v1/compras/catalogos/tipos-documento-oc';
      const { data } = await apiRequest<TipoDocumentoOcItem[]>(path, {
        signal,
      });
      return data;
    },
    staleTime: STALE,
    gcTime: GC,
  });
}

/**
 * <c>useUsosPrincipales()</c> — catálogo seedeado en UF2-PR1 (8
 * usos comunes en non-producción industrial). Mismo patrón que
 * <c>useCondicionesPago</c>.
 */
export function useUsosPrincipales(opts: { includeInactivas?: boolean } = {}) {
  return useQuery({
    queryKey: catalogosKeys.usosPrincipales(),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (!opts.includeInactivas) {
        params.set('estatus', String(EstatusCatalogo.Activo));
      }
      const qs = params.toString();
      const path = qs
        ? `/api/v1/catalogos/usos-principales?${qs}`
        : '/api/v1/catalogos/usos-principales';
      const { data } = await apiRequest<UsoPrincipalItem[]>(path, { signal });
      return data;
    },
    staleTime: STALE,
    gcTime: GC,
  });
}

/**
 * Helper: convierte un <c>PagedCatalogoResponse&lt;T&gt;</c> a un
 * <c>Map&lt;id, item&gt;</c> para lookup constante por id en tablas.
 * Devuelve <c>Map</c> vacío si la query falló (ej. 403 sin permiso).
 *
 * @example
 * ```ts
 * const deptosQuery = useDepartamentos();
 * const deptosMap = mapById(deptosQuery.data?.items);
 * const nombre = deptosMap.get(rq.departamentoId)?.nombre ?? rq.departamentoId;
 * ```
 */
export function mapById<T extends { id: string }>(
  items: T[] | undefined,
): Map<string, T> {
  return new Map((items ?? []).map((i) => [i.id, i]));
}

/**
 * Type guard útil para que las pantallas decidan si fallback al id raw
 * cuando un catálogo falla con 403.
 */
export function esCatalogoSinPermiso(error: unknown): boolean {
  return esApiError(error) && error.status === 403;
}
