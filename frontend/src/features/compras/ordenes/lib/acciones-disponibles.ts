import {
  EstadoOrdenCompra,
  SubEstadoRecepcion,
  type OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * <b>Fuente única</b> de la lógica condicional de acciones en P3
 * (detalle de OC). Cualquier botón / item de menú deriva su
 * <c>visible</c> y <c>habilitada</c> de aquí, no de hardcoded
 * <c>if estado === ...</c> dispersos por el código.
 *
 * <para>Mirror exacto de la <b>matriz §6.1</b> del doc 05 de OC. Tests
 * parametrizados recorren toda la matriz (cada celda × cada permiso →
 * outcome esperado). Si la matriz cambia, este archivo y los tests se
 * actualizan en el mismo PR.</para>
 *
 * <para>Convención de la tabla original:</para>
 * <list>
 *   <item>✅ <c>{ visible: true, habilitada: true }</c> — aparece y
 *   clickable.</item>
 *   <item>⚪ <c>{ visible: true, habilitada: false, motivoDeshabilitada }</c>
 *   — aparece deshabilitada con tooltip explicativo.</item>
 *   <item>❌ <c>{ visible: false, habilitada: false }</c> — no
 *   aparece.</item>
 * </list>
 *
 * <para>Sin el permiso de la columna "Permiso" → ❌ siempre. Esto
 * evita que la UI muestre algo que el backend rechazaría con 403.</para>
 *
 * <para><b>Convenciones especiales</b>:</para>
 * <list>
 *   <item><c>Agregar línea (manual)</c>: requiere
 *   <c>oc.sinRequisicionPrevia=true</c> además del estado válido.
 *   En OCs con RQ las líneas vienen heredadas — no se agregan a
 *   mano.</item>
 *   <item><c>Cancelar 1 firma vs doble firma</c>: depende del
 *   sub-estado de recepción de la OC. <c>SinRecepcion</c> → 1 firma
 *   suficiente (incluso en Autorizada); <c>Parcial</c>/<c>Completa</c>
 *   → exige doble firma (ver §10.5 backend).</item>
 *   <item><c>Editar info importación</c>: dos variantes — completa
 *   (solo Borrador/Rechazada) y solo <c>NumeroPedimento</c>
 *   (también Autorizada para captura aduanal post-aut).</item>
 * </list>
 */

export interface AccionDisponible {
  /** El elemento se renderiza en el DOM. */
  visible: boolean;
  /** El elemento está clickable (cuando <c>visible=true</c>). */
  habilitada: boolean;
  /** Tooltip explicativo cuando <c>visible=true</c> pero
   * <c>habilitada=false</c>. <c>undefined</c> si está habilitada o
   * invisible. */
  motivoDeshabilitada?: string;
}

const OCULTO: AccionDisponible = Object.freeze({
  visible: false,
  habilitada: false,
});

const HABILITADA: AccionDisponible = Object.freeze({
  visible: true,
  habilitada: true,
});

const DISABLED = (motivo: string): AccionDisponible => ({
  visible: true,
  habilitada: false,
  motivoDeshabilitada: motivo,
});

/**
 * Helper interno: si el usuario no tiene NINGUNO de los permisos
 * provistos, oculta el botón. Esto evita ruido visual para usuarios
 * que estructuralmente no pueden hacer la acción.
 */
function gatePermisos(
  permisos: readonly string[],
  permisosRequeridos: readonly string[],
  resultado: AccionDisponible,
): AccionDisponible {
  const tieneAlguno = permisosRequeridos.some((p) => permisos.includes(p));
  if (!tieneAlguno) return OCULTO;
  return resultado;
}

// ============================================================================
// Bloque 1 — acciones sobre cabecera + líneas (gateadas por `crear`)
// ============================================================================

/**
 * <b>Editar cabecera</b> — solo en <c>Borrador</c> y <c>Rechazada</c>
 * (post-rechazo, vuelve a editable). Permiso: <c>crear</c>.
 */
export function accionEditarCabecera(
  oc: Pick<OrdenCompraDetalleResponse, 'estado'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesCrear],
    oc.estado === EstadoOrdenCompra.Borrador ||
      oc.estado === EstadoOrdenCompra.Rechazada
      ? HABILITADA
      : OCULTO,
  );
}

/**
 * <b>Agregar línea (manual)</b> — solo en <c>Borrador</c>/<c>Rechazada</c>
 * Y solo si la OC es <c>SinRequisicionPrevia=true</c>. Permiso:
 * <c>crear</c>. En OCs con RQ las líneas vienen heredadas, no se
 * agregan manualmente (política §3.bis.4).
 */
export function accionAgregarLineaManual(
  oc: Pick<OrdenCompraDetalleResponse, 'estado' | 'sinRequisicionPrevia'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesCrear],
    (oc.estado === EstadoOrdenCompra.Borrador ||
      oc.estado === EstadoOrdenCompra.Rechazada) &&
      oc.sinRequisicionPrevia
      ? HABILITADA
      : OCULTO,
  );
}

/**
 * <b>Editar línea (estructural)</b> — cantidad/precio/etc. Solo en
 * <c>Borrador</c>/<c>Rechazada</c>. Permiso: <c>crear</c>.
 * Restricción adicional (no aquí, en el editor): líneas con
 * <c>requisicionId</c> ≠ null tienen cantidad/artículo bloqueados;
 * solo precio editable.
 */
export function accionEditarLineaEstructural(
  oc: Pick<OrdenCompraDetalleResponse, 'estado'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesCrear],
    oc.estado === EstadoOrdenCompra.Borrador ||
      oc.estado === EstadoOrdenCompra.Rechazada
      ? HABILITADA
      : OCULTO,
  );
}

/**
 * <b>Eliminar línea</b> — solo en <c>Borrador</c>/<c>Rechazada</c>.
 * Permiso: <c>crear</c>.
 */
export function accionEliminarLinea(
  oc: Pick<OrdenCompraDetalleResponse, 'estado'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesCrear],
    oc.estado === EstadoOrdenCompra.Borrador ||
      oc.estado === EstadoOrdenCompra.Rechazada
      ? HABILITADA
      : OCULTO,
  );
}

/**
 * <b>Editar texto adicional de línea</b> — campo libre, no
 * estructural. Editable en cualquier estado no-terminal:
 * <c>Borrador</c>/<c>EnAutorizacion*</c>/<c>Autorizada</c>/
 * <c>Rechazada</c>. Permiso: <c>crear</c>.
 */
export function accionEditarTextoAdicionalLinea(
  oc: Pick<OrdenCompraDetalleResponse, 'estado'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesCrear],
    oc.estado === EstadoOrdenCompra.Cerrada ||
      oc.estado === EstadoOrdenCompra.Cancelada
      ? OCULTO
      : HABILITADA,
  );
}

// ============================================================================
// Bloque 2 — adjuntos
// ============================================================================

/**
 * <b>Adjuntar documento</b> — en cualquier estado no-terminal.
 * Permiso: <c>adjuntar</c>.
 */
export function accionAdjuntarDocumento(
  oc: Pick<OrdenCompraDetalleResponse, 'estado'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesAdjuntar],
    oc.estado === EstadoOrdenCompra.Cerrada ||
      oc.estado === EstadoOrdenCompra.Cancelada
      ? OCULTO
      : HABILITADA,
  );
}

/**
 * <b>Remover adjunto</b> — solo en <c>Borrador</c>/<c>Rechazada</c>.
 * Permiso: <c>crear</c> (no <c>adjuntar</c>: remover es destructivo).
 */
export function accionRemoverAdjunto(
  oc: Pick<OrdenCompraDetalleResponse, 'estado'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesCrear],
    oc.estado === EstadoOrdenCompra.Borrador ||
      oc.estado === EstadoOrdenCompra.Rechazada
      ? HABILITADA
      : OCULTO,
  );
}

// ============================================================================
// Bloque 3 — información logística e importación
// ============================================================================

/**
 * <b>Editar info logística</b> (dirección, transportista, guía,
 * instrucciones) — en cualquier estado no-terminal. Doc 05 §4.6:
 * info logística es editable post-aut sin re-auth. Permisos:
 * <c>crear</c> O <c>logistica</c> (perfil específico).
 */
export function accionEditarInfoLogistica(
  oc: Pick<OrdenCompraDetalleResponse, 'estado'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [
      PermisosCanonicos.ComprasOrdenesCrear,
      PermisosCanonicos.ComprasOrdenesLogistica,
    ],
    oc.estado === EstadoOrdenCompra.Cerrada ||
      oc.estado === EstadoOrdenCompra.Cancelada
      ? OCULTO
      : HABILITADA,
  );
}

/**
 * <b>Editar info importación (campos completos)</b> — incoterm, país
 * origen, contenedor, ruta, semana, pedimento. Solo en
 * <c>Borrador</c>/<c>Rechazada</c>. Permisos: <c>crear</c> O
 * <c>logistica</c>. <c>NumeroPedimento</c> tiene su propia acción
 * más permisiva (acción siguiente).
 *
 * <para>Si la OC NO es de importación (<c>esImportacion=false</c>),
 * la acción está oculta — el form de importación no se renderiza.</para>
 */
export function accionEditarInfoImportacion(
  oc: Pick<OrdenCompraDetalleResponse, 'estado' | 'esImportacion'>,
  permisos: readonly string[],
): AccionDisponible {
  if (!oc.esImportacion) return OCULTO;
  return gatePermisos(
    permisos,
    [
      PermisosCanonicos.ComprasOrdenesCrear,
      PermisosCanonicos.ComprasOrdenesLogistica,
    ],
    oc.estado === EstadoOrdenCompra.Borrador ||
      oc.estado === EstadoOrdenCompra.Rechazada
      ? HABILITADA
      : OCULTO,
  );
}

/**
 * <b>Editar NumeroPedimento (post-autorización)</b> — único campo de
 * info importación editable en <c>Autorizada</c> sin re-auth (doc 05
 * §4.6 + matriz §6.1). El comprador captura el pedimento aduanal
 * cuando llega; modificarlo no requiere re-firma.
 *
 * <para>Si la OC NO es de importación, oculto.</para>
 */
export function accionEditarNumeroPedimentoImportacion(
  oc: Pick<OrdenCompraDetalleResponse, 'estado' | 'esImportacion'>,
  permisos: readonly string[],
): AccionDisponible {
  if (!oc.esImportacion) return OCULTO;
  return gatePermisos(
    permisos,
    [
      PermisosCanonicos.ComprasOrdenesCrear,
      PermisosCanonicos.ComprasOrdenesLogistica,
    ],
    oc.estado === EstadoOrdenCompra.Cerrada ||
      oc.estado === EstadoOrdenCompra.Cancelada
      ? OCULTO
      : HABILITADA,
  );
}

// ============================================================================
// Bloque 4 — workflow de autorización
// ============================================================================

/**
 * <b>Transmitir a autorización</b> — pasa de <c>Borrador</c> a
 * <c>EnAutorizacionJefeCompras</c>. Permiso: <c>crear</c>. También
 * disponible desde <c>Rechazada</c> (re-transmitir tras correcciones).
 *
 * <para>El backend valida pre-condiciones (al menos 1 línea, cabecera
 * completa, etc., §7.1). Si falla, el botón se queda visible pero el
 * caller debería mostrar el motivo en la respuesta del intento. Acá
 * NO bloqueamos por pre-condiciones — el caller pre-valida si quiere
 * UX óptima (deshabilitar con tooltip).</para>
 */
export function accionTransmitirAAutorizacion(
  oc: Pick<OrdenCompraDetalleResponse, 'estado'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesCrear],
    oc.estado === EstadoOrdenCompra.Borrador ||
      oc.estado === EstadoOrdenCompra.Rechazada
      ? HABILITADA
      : OCULTO,
  );
}

/**
 * <b>Aprobar Nivel 1 (Jefe de Compras)</b> — solo en
 * <c>EnAutorizacionJefeCompras</c>. Permiso: <c>autorizar.nivel1</c>.
 *
 * <para><b>Aprobar N2 nunca aparece antes de N1</b>: si la OC sigue
 * en N1, el botón Nivel 2 está oculto (no deshabilitado con tooltip
 * — pasa a OCULTO). Esto es intencional: la matriz dice N2 = ❌ en
 * <c>EnAutorizJefeCompras</c>.</para>
 */
export function accionAprobarNivel1(
  oc: Pick<OrdenCompraDetalleResponse, 'estado'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesAutorizarNivel1],
    oc.estado === EstadoOrdenCompra.EnAutorizacionJefeCompras
      ? HABILITADA
      : OCULTO,
  );
}

/**
 * <b>Aprobar Nivel 2 (Dirección)</b> — solo en
 * <c>EnAutorizacionDireccion</c>. Permiso: <c>autorizar.nivel2</c>.
 */
export function accionAprobarNivel2(
  oc: Pick<OrdenCompraDetalleResponse, 'estado'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesAutorizarNivel2],
    oc.estado === EstadoOrdenCompra.EnAutorizacionDireccion
      ? HABILITADA
      : OCULTO,
  );
}

/**
 * <b>Rechazar</b> — disponible en cualquier <c>EnAutorizacion*</c>.
 * Permisos: <c>autorizar.nivel1</c> O <c>autorizar.nivel2</c>
 * (cualquier autorizador puede rechazar en su nivel).
 */
export function accionRechazar(
  oc: Pick<OrdenCompraDetalleResponse, 'estado'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [
      PermisosCanonicos.ComprasOrdenesAutorizarNivel1,
      PermisosCanonicos.ComprasOrdenesAutorizarNivel2,
    ],
    oc.estado === EstadoOrdenCompra.EnAutorizacionJefeCompras ||
      oc.estado === EstadoOrdenCompra.EnAutorizacionDireccion
      ? HABILITADA
      : OCULTO,
  );
}

// ============================================================================
// Bloque 5 — cancelación + duplicación
// ============================================================================

/**
 * <b>Cancelar (1 firma)</b> — disponible en estados no-terminales,
 * EXCEPTO <c>Autorizada con recepciones parciales/completas</c> (que
 * requiere doble firma, ver acción siguiente). Permiso:
 * <c>cancelar</c>.
 */
export function accionCancelar1Firma(
  oc: Pick<OrdenCompraDetalleResponse, 'estado' | 'subEstadoRecepcion'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesCancelar],
    (() => {
      if (
        oc.estado === EstadoOrdenCompra.Cerrada ||
        oc.estado === EstadoOrdenCompra.Cancelada
      )
        return OCULTO;
      // Autorizada SIN recepciones → 1 firma OK.
      // Autorizada CON recepciones → exclusivo doble firma.
      if (
        oc.estado === EstadoOrdenCompra.Autorizada &&
        oc.subEstadoRecepcion !== SubEstadoRecepcion.SinRecepcion
      )
        return OCULTO;
      return HABILITADA;
    })(),
  );
}

/**
 * <b>Cancelar (doble firma con recepciones)</b> — solo en
 * <c>Autorizada con sub_estado_recepcion ≠ SinRecepcion</c>.
 * Requiere los 3 permisos: <c>cancelar.doble</c> +
 * <c>autorizar.nivel1</c> + <c>autorizar.nivel2</c> (los firmantes
 * deben ser usuarios distintos al click; el dialog de doble firma lo
 * valida).
 *
 * <para>Aquí gateamos solo <c>cancelar.doble</c>; los otros 2 los
 * valida el dialog/handler porque son del flujo de selección de
 * autorizadores delegados.</para>
 */
export function accionCancelarDobleFirma(
  oc: Pick<OrdenCompraDetalleResponse, 'estado' | 'subEstadoRecepcion'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesCancelarDoble],
    oc.estado === EstadoOrdenCompra.Autorizada &&
      oc.subEstadoRecepcion !== SubEstadoRecepcion.SinRecepcion
      ? HABILITADA
      : OCULTO,
  );
}

/**
 * <b>Duplicar OC</b> — único path de modificación post-autorización
 * (decisión C4 cerrada). Solo desde <c>Cancelada</c>/<c>Rechazada</c>
 * (terminales destructivos). En estados no terminales, OCULTO con
 * intencionalidad: el usuario no debe ver "Duplicar" mientras la OC
 * sigue viva.
 *
 * <para>Permiso: <c>crear</c> (genera nueva OC en Borrador).</para>
 */
export function accionDuplicarOc(
  oc: Pick<OrdenCompraDetalleResponse, 'estado'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesCrear],
    oc.estado === EstadoOrdenCompra.Cancelada ||
      oc.estado === EstadoOrdenCompra.Rechazada
      ? HABILITADA
      : OCULTO,
  );
}

// ============================================================================
// Bloque 6 — read-only / utilities
// ============================================================================

/**
 * <b>Descargar PDF</b> — generado al autorizar N2. Disponible en
 * <c>Autorizada</c> y <c>Cerrada</c>. En <c>Cancelada</c>: visible
 * pero deshabilitado con tooltip "histórico, último PDF antes de
 * cancelar". En <c>Rechazada</c>/<c>EnAutorizacion*</c>/<c>Borrador</c>:
 * oculto (no hay PDF). Permiso: <c>leer</c>.
 */
export function accionDescargarPdf(
  oc: Pick<OrdenCompraDetalleResponse, 'estado'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesLeer],
    (() => {
      if (
        oc.estado === EstadoOrdenCompra.Autorizada ||
        oc.estado === EstadoOrdenCompra.Cerrada
      )
        return HABILITADA;
      if (oc.estado === EstadoOrdenCompra.Cancelada)
        return DISABLED(
          'PDF histórico — generado antes de la cancelación. La descarga seguirá disponible para auditoría.',
        );
      return OCULTO;
    })(),
  );
}

/**
 * <b>Ver árbol de documentos</b> — vista grafo bidireccional
 * (RQ ← OC ← Recepción ← Factura ← Pago). Habilitada cuando la OC
 * tiene descendientes potenciales (Autorizada+); deshabilitada en
 * estados pre-aut o terminales rechazados. Permiso: <c>leer</c>.
 */
export function accionVerArbolDocumentos(
  oc: Pick<OrdenCompraDetalleResponse, 'estado'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesLeer],
    (() => {
      if (
        oc.estado === EstadoOrdenCompra.Autorizada ||
        oc.estado === EstadoOrdenCompra.Cerrada ||
        oc.estado === EstadoOrdenCompra.Cancelada
      )
        return HABILITADA;
      return DISABLED(
        'Sin descendientes mientras la OC siga en pre-autorización o rechazada.',
      );
    })(),
  );
}

/**
 * <b>Ver detalle (read-only)</b> — siempre visible y habilitado para
 * cualquier estado, gateado solo por <c>leer</c>. Es el "default" del
 * detalle de OC. Esta función existe para coherencia cross-acciones
 * (todos los enlaces "Ver" en bandejas / aside lists pueden gatearse
 * uniformemente).
 */
export function accionVerDetalleReadOnly(
  _oc: Pick<OrdenCompraDetalleResponse, 'estado'>,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermisos(
    permisos,
    [PermisosCanonicos.ComprasOrdenesLeer],
    HABILITADA,
  );
}

// ============================================================================
// Lista canónica de las 20 acciones del §6.1 — útil para tests
// parametrizados que recorran toda la matriz.
// ============================================================================

export type AccionFn = (
  oc: OrdenCompraDetalleResponse,
  permisos: readonly string[],
) => AccionDisponible;

/**
 * Las 20 acciones del §6.1 con su nombre canónico (label en
 * español). Útil para iterar en tests y para componentes que
 * agrupan acciones (toolbar del detalle, menú overflow, etc.).
 */
export const ACCIONES_OC: ReadonlyArray<{
  id: string;
  label: string;
  fn: AccionFn;
}> = [
  { id: 'editar-cabecera', label: 'Editar cabecera', fn: accionEditarCabecera },
  {
    id: 'agregar-linea-manual',
    label: 'Agregar línea (manual)',
    fn: accionAgregarLineaManual,
  },
  {
    id: 'editar-linea-estructural',
    label: 'Editar línea (estructural)',
    fn: accionEditarLineaEstructural,
  },
  { id: 'eliminar-linea', label: 'Eliminar línea', fn: accionEliminarLinea },
  {
    id: 'editar-texto-adicional-linea',
    label: 'Editar texto adicional de línea',
    fn: accionEditarTextoAdicionalLinea,
  },
  {
    id: 'adjuntar-documento',
    label: 'Adjuntar documento',
    fn: accionAdjuntarDocumento,
  },
  {
    id: 'remover-adjunto',
    label: 'Remover adjunto',
    fn: accionRemoverAdjunto,
  },
  {
    id: 'editar-info-logistica',
    label: 'Editar info logística',
    fn: accionEditarInfoLogistica,
  },
  {
    id: 'editar-info-importacion',
    label: 'Editar info importación',
    fn: accionEditarInfoImportacion,
  },
  {
    id: 'editar-numero-pedimento-importacion',
    label: 'Editar número de pedimento (importación)',
    fn: accionEditarNumeroPedimentoImportacion,
  },
  {
    id: 'transmitir-a-autorizacion',
    label: 'Transmitir a autorización',
    fn: accionTransmitirAAutorizacion,
  },
  { id: 'aprobar-nivel-1', label: 'Aprobar Nivel 1', fn: accionAprobarNivel1 },
  { id: 'aprobar-nivel-2', label: 'Aprobar Nivel 2', fn: accionAprobarNivel2 },
  { id: 'rechazar', label: 'Rechazar', fn: accionRechazar },
  {
    id: 'cancelar-1-firma',
    label: 'Cancelar (1 firma)',
    fn: accionCancelar1Firma,
  },
  {
    id: 'cancelar-doble-firma',
    label: 'Cancelar (doble firma)',
    fn: accionCancelarDobleFirma,
  },
  { id: 'duplicar-oc', label: 'Duplicar OC', fn: accionDuplicarOc },
  { id: 'descargar-pdf', label: 'Descargar PDF', fn: accionDescargarPdf },
  {
    id: 'ver-arbol-documentos',
    label: 'Ver árbol documentos',
    fn: accionVerArbolDocumentos,
  },
  // 21ª — incluida por coherencia, no en la matriz §6.1 estrictamente:
  {
    id: 'ver-detalle-read-only',
    label: 'Ver detalle (read-only)',
    fn: accionVerDetalleReadOnly,
  },
];
