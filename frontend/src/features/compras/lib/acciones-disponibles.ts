import {
  EstadoRequisicion,
  NivelAutorizacion,
  type RequisicionResponse,
} from '@/features/compras/api/types';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * <b>Fuente única</b> de la lógica condicional de acciones en P3
 * (detalle de requisición). Cualquier botón / item de menú deriva su
 * <c>visible</c> y <c>habilitada</c> de aquí, no de hardcoded
 * <c>if estado === ...</c> dispersos por el código.
 *
 * <para>Mirror exacto de la <b>matriz §6.1</b> del doc 05. Tests
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
 * <para>Sin el permiso de la columna "Permiso" → ❌ siempre
 * (visible:false). Esto evita que la UI muestre algo que el backend
 * rechazaría con 403.</para>
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
 * ¿Existe ya una firma del nivel dado, sin importar quién la dio? Fuente
 * única de "ese nivel ya está satisfecho". La usan <c>accionAprobarNivel1</c>
 * (para ocultarse cuando N1 ya está firmado) y <c>accionAprobarNivel2</c>
 * (para saber si N1 ya está, prerequisito de N2). El dominio garantiza a lo
 * sumo una firma por nivel (UNIQUE RequisicionId+Nivel).
 */
const tieneFirmaNivel = (
  rq: RequisicionResponse,
  nivel: NivelAutorizacion,
): boolean => rq.autorizaciones.some((a) => a.nivel === nivel);

/**
 * Helper interno: si el usuario no tiene el permiso, oculta el botón
 * (no es deshabilitado — es invisible). Esto evita ruido visual para
 * usuarios que estructuralmente no pueden hacer la acción.
 */
function gatePermiso(
  permisos: readonly string[],
  permiso: string,
  resultado: AccionDisponible,
): AccionDisponible {
  if (!permisos.includes(permiso)) return OCULTO;
  return resultado;
}

// ============================================================================
// Acciones sobre la cabecera y la RQ
// ============================================================================

/**
 * <b>Editar cabecera</b> — solo en <c>Borrador</c>. Doc 05 §6.1
 * marca §14.2 como ⚠️: el endpoint <c>PATCH cabecera</c> existe en
 * backend (B.4 mergeado), así que la acción está habilitada cuando
 * el estado lo permite.
 *
 * <para>Permiso: <c>compras.requisiciones.editar</c>.</para>
 */
export function accionEditarCabecera(
  rq: RequisicionResponse,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermiso(
    permisos,
    PermisosCanonicos.ComprasRequisicionesEditar,
    rq.estado === EstadoRequisicion.Borrador
      ? HABILITADA
      : OCULTO,
  );
}

/**
 * <b>Agregar línea</b> — solo en <c>Borrador</c>. Permiso:
 * <c>compras.requisiciones.editar</c>.
 */
export function accionAgregarLinea(
  rq: RequisicionResponse,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermiso(
    permisos,
    PermisosCanonicos.ComprasRequisicionesEditar,
    rq.estado === EstadoRequisicion.Borrador ? HABILITADA : OCULTO,
  );
}

/**
 * <b>Editar línea (estructural)</b> — cantidad, precio, artículo,
 * etc. Solo en <c>Borrador</c>; post-transmisión las cantidades
 * quedan congeladas (excepción: notas, ver
 * <see cref="accionEditarNotasLinea"/>). Permiso:
 * <c>compras.requisiciones.editar</c>.
 */
export function accionEditarLinea(
  rq: RequisicionResponse,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermiso(
    permisos,
    PermisosCanonicos.ComprasRequisicionesEditar,
    rq.estado === EstadoRequisicion.Borrador ? HABILITADA : OCULTO,
  );
}

/**
 * <b>Eliminar línea</b> — solo en <c>Borrador</c>. Permiso:
 * <c>compras.requisiciones.editar</c>. La RQ debe quedar con al menos
 * una línea para transmitirse; el chequeo lo hace el handler de
 * Transmitir (<c>TRANSMITIR_SIN_LINEAS</c>). El frontend permite
 * dejarla en 0 líneas; el botón Transmitir se gate por aparte.
 */
export function accionEliminarLinea(
  rq: RequisicionResponse,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermiso(
    permisos,
    PermisosCanonicos.ComprasRequisicionesEditar,
    rq.estado === EstadoRequisicion.Borrador ? HABILITADA : OCULTO,
  );
}

/**
 * <b>Editar notas de línea</b> — caso especial: el comprador puede
 * editar notas POST-autorización (caso de uso típico: agregar
 * observación al proveedor). Disponible en
 * <c>Borrador</c>/<c>EnAutorizacion</c>/<c>Autorizada</c>/<c>EnSurtido</c>;
 * read-only en estados terminales. Permiso:
 * <c>compras.requisiciones.editar</c>.
 */
export function accionEditarNotasLinea(
  rq: RequisicionResponse,
  permisos: readonly string[],
): AccionDisponible {
  if (!permisos.includes(PermisosCanonicos.ComprasRequisicionesEditar)) {
    return OCULTO;
  }
  const estadosNoTerminalesEditables: EstadoRequisicion[] = [
    EstadoRequisicion.Borrador,
    EstadoRequisicion.EnAutorizacion,
    EstadoRequisicion.Autorizada,
    EstadoRequisicion.EnSurtido,
  ];
  return estadosNoTerminalesEditables.includes(rq.estado)
    ? HABILITADA
    : OCULTO;
}

/**
 * <b>Transmitir</b> — manda la RQ a <c>EnAutorizacion</c>. Solo en
 * <c>Borrador</c>; deshabilitada con tooltip si la RQ tiene 0 líneas
 * (el backend rechazaría con <c>TRANSMITIR_SIN_LINEAS</c>). Permiso:
 * <c>compras.requisiciones.editar</c> (mismo que crear/editar).
 */
export function accionTransmitir(
  rq: RequisicionResponse,
  permisos: readonly string[],
): AccionDisponible {
  if (!permisos.includes(PermisosCanonicos.ComprasRequisicionesEditar)) {
    return OCULTO;
  }
  if (rq.estado !== EstadoRequisicion.Borrador) return OCULTO;
  if (rq.lineas.length === 0) {
    return DISABLED(
      'Agrega al menos una línea antes de transmitir a autorización.',
    );
  }
  return HABILITADA;
}

/**
 * <b>Aprobar Nivel1</b> — solo en <c>EnAutorizacion</c>; se oculta si
 * Nivel1 ya está firmado por <b>cualquiera</b> (no solo por el usuario
 * actual). El dominio impide un 2º N1 (UNIQUE RequisicionId+Nivel), así
 * que ofrecer el botón cuando N1 ya está dado sería un dead-end garantizado
 * (<c>AUTORIZACION_NIVEL_DUPLICADO</c>). Simétrico con
 * <c>accionAprobarNivel2</c>, que ya consulta la misma firma N1 vía
 * <c>tieneFirmaNivel</c>. Permiso:
 * <c>compras.requisiciones.autorizar-nivel1</c>.
 */
export function accionAprobarNivel1(
  rq: RequisicionResponse,
  permisos: readonly string[],
): AccionDisponible {
  if (
    !permisos.includes(PermisosCanonicos.ComprasRequisicionesAutorizarNivel1)
  ) {
    return OCULTO;
  }
  if (rq.estado !== EstadoRequisicion.EnAutorizacion) return OCULTO;
  if (tieneFirmaNivel(rq, NivelAutorizacion.Nivel1)) return OCULTO;
  return HABILITADA;
}

/**
 * <b>Aprobar Nivel2</b> — solo en <c>EnAutorizacion</c>, requiere
 * que Nivel1 ya esté firmado. Si N1 no está firmado, **se muestra
 * deshabilitada** (no oculta) con tooltip explicativo, para que el
 * autorizador N2 entienda por qué no puede aún. Permiso:
 * <c>compras.requisiciones.autorizar-nivel2</c>.
 */
export function accionAprobarNivel2(
  rq: RequisicionResponse,
  permisos: readonly string[],
  currentUserId: string,
): AccionDisponible {
  if (
    !permisos.includes(PermisosCanonicos.ComprasRequisicionesAutorizarNivel2)
  ) {
    return OCULTO;
  }
  if (rq.estado !== EstadoRequisicion.EnAutorizacion) return OCULTO;
  const yaFirmoN2 = rq.autorizaciones.some(
    (a) =>
      a.nivel === NivelAutorizacion.Nivel2 && a.usuarioId === currentUserId,
  );
  if (yaFirmoN2) return OCULTO;
  const n1Firmado = tieneFirmaNivel(rq, NivelAutorizacion.Nivel1);
  if (!n1Firmado) {
    return DISABLED('Falta autorización Nivel 1');
  }
  return HABILITADA;
}

/**
 * <b>Rechazar</b> — solo en <c>EnAutorizacion</c>. Permiso:
 * <c>compras.requisiciones.rechazar</c>.
 */
export function accionRechazar(
  rq: RequisicionResponse,
  permisos: readonly string[],
): AccionDisponible {
  return gatePermiso(
    permisos,
    PermisosCanonicos.ComprasRequisicionesRechazar,
    rq.estado === EstadoRequisicion.EnAutorizacion ? HABILITADA : OCULTO,
  );
}

/**
 * <b>Eliminar (RQ)</b> — pre-autorización solamente. Disponible en
 * <c>Borrador</c> y <c>EnAutorizacion</c>. Permiso:
 * <c>compras.requisiciones.eliminar</c>.
 */
export function accionEliminarRequisicion(
  rq: RequisicionResponse,
  permisos: readonly string[],
): AccionDisponible {
  if (!permisos.includes(PermisosCanonicos.ComprasRequisicionesEliminar)) {
    return OCULTO;
  }
  const eliminable: EstadoRequisicion[] = [
    EstadoRequisicion.Borrador,
    EstadoRequisicion.EnAutorizacion,
  ];
  return eliminable.includes(rq.estado) ? HABILITADA : OCULTO;
}

/**
 * <b>Cancelar</b> — post-autorización. Disponible en
 * <c>Autorizada</c> y <c>EnSurtido</c>. Libera reservas y aborta OCs
 * borrador (lo decide el handler backend). Permiso:
 * <c>compras.requisiciones.cancelar</c>.
 */
export function accionCancelar(
  rq: RequisicionResponse,
  permisos: readonly string[],
): AccionDisponible {
  if (!permisos.includes(PermisosCanonicos.ComprasRequisicionesCancelar)) {
    return OCULTO;
  }
  const cancelable: EstadoRequisicion[] = [
    EstadoRequisicion.Autorizada,
    EstadoRequisicion.EnSurtido,
  ];
  return cancelable.includes(rq.estado) ? HABILITADA : OCULTO;
}

/**
 * <b>Cerrar manual</b> (ADR-0043) — el jefe de almacén / almacenista cierra
 * una RQ que el requisitante ya no necesita. Disponible en <c>Autorizada</c>
 * y <c>EnSurtido</c> (mismo origen que Cancelar). El backend deriva el
 * terminal (<c>CerradaSinSurtir</c> / <c>CerradaSurtidaParcial</c>). Permiso:
 * <c>compras.requisiciones.cerrar-manual</c> (sembrado a jefe-almacén /
 * almacenista).
 */
export function accionCerrarManual(
  rq: RequisicionResponse,
  permisos: readonly string[],
): AccionDisponible {
  if (!permisos.includes(PermisosCanonicos.ComprasRequisicionesCerrarManual)) {
    return OCULTO;
  }
  const cerrable: EstadoRequisicion[] = [
    EstadoRequisicion.Autorizada,
    EstadoRequisicion.EnSurtido,
  ];
  return cerrable.includes(rq.estado) ? HABILITADA : OCULTO;
}

/**
 * <b>Convertir a OC</b> (PR-B 2026-05-13) — el comprador convierte
 * manualmente esta RQ en una OC (modo 1:1). Solo visible cuando:
 *
 * <list>
 *   <item>Setting <c>autoGenerarOcAlAutorizar</c> está <c>false</c>
 *   (modo manual). Si <c>true</c>, la OC borrador ya se generó al
 *   autorizar — no aplica.</item>
 *   <item>Estado <c>EnSurtido</c> — la RQ ya pasó por la bifurcación y
 *   tiene saldo de compra.</item>
 *   <item><c>comprometidaEnOcId == null</c> — la RQ aún no está
 *   asignada a una OC activa.</item>
 *   <item>Permiso <c>compras.ordenes.crear</c>.</item>
 * </list>
 *
 * <para>
 * <c>autoGenerarOcAlAutorizar=null</c> (settings no cargados todavía)
 * se trata como "no mostrar aún" — comportamiento conservador.
 * </para>
 */
export function accionConvertirAOc(
  rq: RequisicionResponse,
  permisos: readonly string[],
  autoGenerarOcAlAutorizar: boolean | null,
): AccionDisponible {
  if (!permisos.includes(PermisosCanonicos.ComprasOrdenesCrear)) return OCULTO;
  if (autoGenerarOcAlAutorizar !== false) return OCULTO;
  if (rq.estado !== EstadoRequisicion.EnSurtido) return OCULTO;
  if (rq.comprometidaEnOcId != null) return OCULTO;
  return HABILITADA;
}

/**
 * <b>Ver detalle (read-only)</b> — siempre visible si el usuario
 * tiene <c>compras.requisiciones.leer</c>, sin importar el estado.
 * Esto cubre la pantalla P3 entera; los demás botones se derivan de
 * las funciones específicas.
 */
export function accionVerDetalle(
  permisos: readonly string[],
): AccionDisponible {
  return permisos.includes(PermisosCanonicos.ComprasRequisicionesLeer)
    ? HABILITADA
    : OCULTO;
}
