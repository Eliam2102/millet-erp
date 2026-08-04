/**
 * Tipos del módulo Centros de Costo (CECO-FE-PR1). Mirror manual de los
 * DTOs del backend (`Millet.CentrosCosto.Application`) — cuando ADR-0017
 * (codegen TS desde OpenAPI) entre, este archivo se vuelve autogenerado.
 */

/**
 * Mirror de `Millet.Catalogos.Domain.EstatusCatalogo` (backend `short`).
 * Mismos valores que los mirrors de los demás features.
 */
export const EstatusCatalogo = {
  Activo: 0,
  Inactivo: 1,
  EnRevision: 2,
} as const;
export type EstatusCatalogo =
  (typeof EstatusCatalogo)[keyof typeof EstatusCatalogo];

/** Tipo del nodo que se EXPANDE en la jerarquía (los hijos son el nivel siguiente). */
export type NodoTipoJerarquia = 'raiz' | 'dim1' | 'dim2';

/**
 * Mirror de `NodoCeCoDto` (jerarquía lazy, CECO-PR3/PR4): un hijo
 * inmediato del nodo expandido. `tipo` viene del backend en código Dim
 * ("dim1" | "dim2" | "dim3") — la etiqueta visible SIEMPRE sale del
 * helper de `lib/etiquetas.ts`, nunca de strings sueltos (07 §0).
 * `grupo` = nombre del GrupoDim2 (nodos dim2) o GrupoDim3 (dim3), null
 * en dim1 — se pinta como chip. Los conteos cuentan VIVOS
 * (estatus != Inactivo), independiente de `incluirInactivos`.
 */
export interface NodoCeCo {
  tipo: 'dim1' | 'dim2' | 'dim3';
  id: string;
  clave: string;
  nombre: string;
  estatus: EstatusCatalogo;
  grupo: string | null;
  esHoja: boolean;
  dim2Vivas: number;
  dim3Vivas: number;
}

// ─── CRUD (CECO-FE-PR2) — mirrors de responses/bodies del backend ──────────
// La versión viaja en el header If-Match (ETag), NUNCA en el body.

/** Mirror de `PagedResponse<T>` del módulo. */
export interface PagedResponse<T> {
  items: T[];
  total: number;
  offset: number;
  limit: number;
}

/** Mirror de `Dim1Response`. */
export interface Dim1Detalle {
  id: string;
  clave: string;
  nombre: string;
  estatus: EstatusCatalogo;
  version: number;
}

/** Mirror de `Dim2Response`. */
export interface Dim2Detalle {
  id: string;
  dim1Id: string;
  clave: string;
  nombre: string;
  grupoDim2Id: string;
  estatus: EstatusCatalogo;
  version: number;
}

/** Mirror de `Dim3Response`. */
export interface Dim3Detalle {
  id: string;
  dim2Id: string;
  clave: string;
  nombre: string;
  grupoDim3Id: string;
  estatus: EstatusCatalogo;
  version: number;
}

/** Mirror de `GrupoDimResponse` (grupos-dim2 y grupos-dim3 comparten forma). */
export interface GrupoDimDetalle {
  id: string;
  nombre: string;
  estatus: EstatusCatalogo;
  version: number;
}

/** Mirror de `Dim2ListItem` (listas planas — trae el grupo resuelto). */
export interface Dim2ListItem {
  id: string;
  dim1Id: string;
  clave: string;
  nombre: string;
  grupoDim2Id: string;
  grupoDim2Nombre: string;
  estatus: EstatusCatalogo;
  version: number;
}

/** Mirror de `Dim3ListItem` (trae Dim2Id — la rama; el Dim1Id se resuelve con el detalle de la Dim2). */
export interface Dim3ListItem {
  id: string;
  dim2Id: string;
  clave: string;
  nombre: string;
  grupoDim3Id: string;
  grupoDim3Nombre: string;
  dim2Clave: string;
  dim2Nombre: string;
  estatus: EstatusCatalogo;
  version: number;
}

/**
 * Mirror de `Millet.CentrosCosto.Application.Catalogo.Dim3BusquedaItem` — el
 * item del selector "Máquina" de la Fase E, con el contexto completo (grupo,
 * Dim2, Dim1) para el display de desambiguación al elegir entre las ~361. Lo
 * devuelven, en array plano (no paginado), tanto el selector FILTRADO
 * (`/dim3/buscar`, con alcance) como el ABIERTO (captura por proxy). Ver
 * ADR-0050.
 */
export interface Dim3BusquedaItem {
  id: string;
  clave: string;
  nombre: string;
  grupoDim3Nombre: string;
  dim2Clave: string;
  dim2Nombre: string;
  dim1Clave: string;
  dim1Nombre: string;
  estatus: EstatusCatalogo;
}

/** Mirror de `DesactivarDim1Response` — los conteos REALES de la cascada (ADR-0049). */
export interface DesactivarDim1Resultado {
  id: string;
  estatus: EstatusCatalogo;
  dim2Desactivadas: number;
  dim3Desactivadas: number;
}

/** Mirror de `DesactivarDim2Response`. */
export interface DesactivarDim2Resultado {
  id: string;
  estatus: EstatusCatalogo;
  dim3Desactivadas: number;
}

// ─── Asignación / tri-estado (FE-PR3) ──────────────────────────────────────
// Enums como NÚMEROS (el Api no tiene JsonStringEnumConverter).

/** Mirror de `TriEstado` (backend short). */
export const TriEstado = {
  Ninguno: 0,
  Parcial: 1,
  Todo: 2,
} as const;
export type TriEstado = (typeof TriEstado)[keyof typeof TriEstado];

/**
 * Mirror de `NivelAlcance` — el nivel del nodo que se marca. Los niveles
 * de grupo llevan además el `grupoId`; el `nodoId` es el PADRE que acota.
 */
export const NivelAlcance = {
  Dim1: 0,
  GrupoDim2BajoDim1: 1,
  Dim2: 2,
  GrupoDim3BajoDim2: 3,
  Dim3: 4,
} as const;
export type NivelAlcance = (typeof NivelAlcance)[keyof typeof NivelAlcance];

/** Hoja Dim3 del árbol de asignación (`Dim3AsignacionDto`). */
export interface Dim3Asignacion {
  id: string;
  clave: string;
  nombre: string;
  asignada: boolean;
}

/** Nodo genérico con tri-estado y conteos (los niveles internos comparten forma). */
interface NodoAsignacionBase {
  id: string;
  nombre: string;
  estado: TriEstado;
  dim3Vivas: number;
  dim3Asignadas: number;
}

/** GrupoDim3 (`GrupoDim3AsignacionDto`). */
export interface GrupoDim3Asignacion extends NodoAsignacionBase {
  dim3s: Dim3Asignacion[];
}

/** Dim2 (`Dim2AsignacionDto`) — tiene clave y agrupa GrupoDim3. */
export interface Dim2Asignacion extends NodoAsignacionBase {
  clave: string;
  grupos: GrupoDim3Asignacion[];
}

/** GrupoDim2 (`GrupoDim2AsignacionDto`). */
export interface GrupoDim2Asignacion extends NodoAsignacionBase {
  dim2s: Dim2Asignacion[];
}

/** Dim1 (`Dim1AsignacionDto`) — raíz, con clave. */
export interface Dim1Asignacion extends NodoAsignacionBase {
  clave: string;
  grupos: GrupoDim2Asignacion[];
}

/** Barra resumen por dimensión (`ResumenAsignacion`). */
export interface ResumenAsignacion {
  dim1Vivas: number;
  dim1Completas: number;
  dim2Vivas: number;
  dim2Completas: number;
  dim3Vivas: number;
  dim3Asignadas: number;
}

/** Respuesta del GET `/asignaciones/{usuarioId}/arbol` (`ArbolAsignacionResponse`). */
export interface ArbolAsignacionResponse {
  usuarioId: string;
  resumen: ResumenAsignacion;
  dim1s: Dim1Asignacion[];
  /** El usuario seleccionado tiene `dim3.leer-todos` (resuelto en el endpoint). */
  esAlcanceTotal: boolean;
}

/** Cuerpo del POST `/asignaciones/{usuarioId}/marcar` (`MarcarAlcanceRequest`). */
export interface MarcarAlcanceRequest {
  nivel: NivelAlcance;
  nodoId: string;
  grupoId: string | null;
  asignar: boolean;
}

/** Respuesta del marcado (`MarcarAlcanceResponse`) — conteos para reconciliar. */
export interface MarcarAlcanceResponse {
  usuarioId: string;
  hojasResueltas: number;
  afectadas: number;
  totalUsuario: number;
}
