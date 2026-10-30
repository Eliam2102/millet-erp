import {
  ComportamientoSerie,
  EstadoAutorizacionCredito,
  ResultadoLiberacion,
  type AutorizacionCreditoResponse,
  type DecisionLiberacionResponse,
  type ReglaLiberacionSerieResponse,
} from '@/features/cxc/api/types';
import { esApiError } from '@/lib/api';

/** C7.3 supuesto (a): 50 % exacto ya es pendiente. */
export const UMBRAL_COBERTURA_AUTORIZACION = 50;

/**
 * Preview de la cobertura (espejo de backend <c>CoberturaCredito</c>):
 * disponible ÷ monto en %, truncado a 2 decimales y acotado a [0, 100].
 * null si no hay datos suficientes.
 */
export function calcularCobertura(
  disponible: number | null | undefined,
  montoPedido: number,
): number | null {
  if (disponible == null || !(montoPedido > 0)) return null;
  if (disponible <= 0) return 0;
  if (disponible >= montoPedido) return 100;
  return Math.trunc((disponible / montoPedido) * 100 * 100 + 1e-9) / 100;
}

/**
 * C7.3 (QA): el selector de override solo ofrece autorizaciones que se
 * pueden consumir: estado Autorizada (no usadas ni canceladas) y con
 * vigencia futura. Las vencidas siguen en estado Autorizada en el backend.
 */
export function autorizacionesConsumibles(
  items: readonly AutorizacionCreditoResponse[],
  ahora: Date = new Date(),
): AutorizacionCreditoResponse[] {
  return items.filter(
    (a) =>
      a.estado === EstadoAutorizacionCredito.Autorizada &&
      new Date(a.vigenteHasta).getTime() > ahora.getTime(),
  );
}

/**
 * Espejo de backend <c>ResolverComportamientoSerieAsync</c>: gana el prefijo
 * activo más largo con el que empieza el folio; sin regla, evalúa crédito.
 */
export function comportamientoSeriePedido(
  pedidoRef: string,
  reglas: readonly ReglaLiberacionSerieResponse[],
): ComportamientoSerie {
  const folio = pedidoRef.trim();
  let mejor: ReglaLiberacionSerieResponse | null = null;
  for (const r of reglas) {
    if (folio.startsWith(r.prefijo) && (mejor == null || r.prefijo.length > mejor.prefijo.length))
      mejor = r;
  }
  return mejor?.comportamiento ?? ComportamientoSerie.EvaluaCredito;
}

/**
 * C7.3 (QA) + supuestos (d) y (e): si la serie exige autorización
 * (NuncaLibera), el override se ofrece siempre, con o sin línea (regla
 * C7.2). Si la serie evalúa crédito, se oculta cuando el crédito alcanza,
 * cuando la cobertura es menor al 50 % y cuando no hay línea activa en la
 * moneda (cobertura null): el backend responde CXC_SIN_LINEA_ACTIVA.
 */
export function mostrarSelectorOverride(
  alcanza: boolean,
  cobertura: number | null,
  comportamiento: ComportamientoSerie = ComportamientoSerie.EvaluaCredito,
): boolean {
  if (comportamiento === ComportamientoSerie.NuncaLibera) return true;
  if (alcanza || cobertura == null) return false;
  return cobertura >= UMBRAL_COBERTURA_AUTORIZACION;
}

/**
 * C7.3 supuesto (e): texto cuando el cliente no tiene línea activa en la
 * moneda (no existe o está bloqueada). En series que exigen autorización
 * el override sigue aplicando; en las que evalúan crédito, no.
 */
export function textoSinLineaActiva(
  cliente: string,
  moneda: string,
  bloqueada: boolean,
  serieExigeAutorizacion: boolean,
): string {
  const linea = bloqueada
    ? `${cliente} tiene la línea de crédito en ${moneda} bloqueada`
    : `${cliente} no tiene línea de crédito activa en ${moneda}`;
  return serieExigeAutorizacion
    ? `${linea} — sin override, la decisión quedará retenida.`
    : `${linea}: el pedido quedará retenido; no procede autorización de CxC.`;
}

/**
 * C7.3 (QA obs. 4): texto de la vista previa de cobertura. Si la serie
 * exige autorización, el crédito no decide y no se dice «no procede
 * autorización» (el selector explica que se libera con override).
 */
export function textoCoberturaPreview(
  alcanza: boolean,
  pendiente: boolean,
  serieExigeAutorizacion: boolean,
): string {
  if (serieExigeAutorizacion)
    return 'La serie de este pedido no se libera por crédito, sin importar la cobertura.';
  if (alcanza) return 'El crédito alcanza para el monto del pedido.';
  if (pendiente)
    return 'El crédito cubre entre 50 % y 99 %: quedará pendiente de autorización de CxC (o usa un override vigente para este pedido).';
  return 'El crédito cubre menos del 50 %: quedará retenido y no procede autorización de CxC.';
}

/**
 * C7.3: «Autorizar» y «Rechazar» solo para pendientes sin resolver, con
 * permiso de override y nunca para quien registró la pendiente (sin
 * autoconsumo; el backend lo vuelve a validar).
 */
export function puedeAutorizarPendiente(
  d: DecisionLiberacionResponse,
  puedeOverride: boolean,
  usuarioActualId: string | null,
): boolean {
  return (
    puedeOverride &&
    d.resultado === ResultadoLiberacion.PendienteAutorizacion &&
    !d.resolucionId &&
    (usuarioActualId == null || d.decididoPor !== usuarioActualId)
  );
}

/**
 * Mensajes en español por código de negocio del backend (C7.3). El title
 * del Problem Details ya viene en español; esto da un texto corto y
 * accionable para los casos esperados.
 */
const MENSAJES_ERROR: Record<string, string> = {
  DL_PENDIENTE_YA_RESUELTA:
    'Esta decisión pendiente ya fue resuelta (autorizada o rechazada) por otro usuario.',
  DL_PENDIENTE_SUPERADA:
    'Hay una decisión más reciente para este pedido; revisa esa en lugar de la pendiente.',
  DL_AUTOCONSUMO:
    'Quien registró la decisión pendiente no puede autorizarla ni rechazarla; debe hacerlo otro gerente de CxC.',
  DL_NO_PENDIENTE: 'La decisión ya no está pendiente de autorización.',
  CXC_COBERTURA_INSUFICIENTE:
    'La línea cubre menos del 50 % del pedido: no procede la autorización y queda retenido.',
  CXC_SIN_LINEA_ACTIVA:
    'El cliente no tiene línea de crédito activa (no existe o está bloqueada): el pedido queda retenido y no procede autorización de CxC.',
  AC_REFERENCIA_DISTINTA:
    'La autorización elegida se otorgó para otro pedido; cada autorización sirve solo para su folio.',
};

/**
 * Texto de error en español para un fallo al autorizar o rechazar una
 * pendiente, o al decidir con override (409/422/otros).
 */
export function mensajeErrorAutorizar(
  error: unknown,
  accion: 'autorizar' | 'rechazar' = 'autorizar',
): { titulo: string; detalle?: string } {
  if (!esApiError(error))
    return {
      titulo:
        accion === 'rechazar'
          ? 'Error inesperado al rechazar la liberación.'
          : 'Error inesperado al autorizar la liberación.',
    };
  const code = error.problem.code;
  if (code && MENSAJES_ERROR[code]) return { titulo: MENSAJES_ERROR[code] };
  if (error.status === 409)
    return { titulo: 'La decisión cambió mientras la revisabas; recarga la bandeja.' };
  if (error.status === 403)
    return {
      titulo:
        accion === 'rechazar'
          ? 'No tienes permiso para rechazar liberaciones.'
          : 'No tienes permiso para autorizar liberaciones.',
    };
  return { titulo: error.problem.title, detalle: error.problem.detail };
}

/**
 * C7.3: al autorizar, el backend recalcula el crédito; el aviso dice lo que
 * realmente pasó con el pedido.
 */
export function avisoResultadoAutorizar(res: DecisionLiberacionResponse): {
  tipo: 'success' | 'warning';
  titulo: string;
  detalle?: string;
} {
  if (res.resultado === ResultadoLiberacion.Liberado)
    return {
      tipo: 'success',
      titulo: `Pedido ${res.pedidoRef}: liberado por crédito.`,
      detalle: 'El crédito disponible ya alcanza para el pedido; no se usó autorización.',
    };
  if (res.resultado === ResultadoLiberacion.Retenido)
    return {
      tipo: 'warning',
      titulo: `Pedido ${res.pedidoRef}: retenido.`,
      detalle:
        res.porcentajeCobertura == null
          ? 'El cliente ya no tiene línea de crédito activa (no existe o está bloqueada).'
          : `La cobertura bajó del 50 % (${Math.trunc(res.porcentajeCobertura)} %); no procede autorización.`,
    };
  return { tipo: 'success', titulo: `Pedido ${res.pedidoRef}: liberado con autorización.` };
}
