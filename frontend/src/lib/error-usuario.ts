import { ApiError, esApiError } from '@/lib/api/error';

const MOTIVOS: Record<string, string> = {
  PORTAL_RFC_NO_COINCIDE: 'El RFC del emisor del CFDI no coincide con el de tu empresa. Revisa el RFC del XML y carga un documento de tu empresa.',
  APARTADO_SIN_PENDIENTE: 'No hay existencia apartada pendiente. Recarga la requisición para revisar lo disponible.',
  CIERRE_CONTEOS_PENDIENTES: 'Hay conteos pendientes en el periodo. Aplícalos o cancélalos antes de cerrar.',
  CIERRE_MOVIMIENTOS_PENDIENTES: 'Hay movimientos pendientes en el periodo. Completa su registro antes de cerrar.',
  CIERRE_VALES_PENDIENTES: 'Hay vales sin regularizar en el periodo. Vincula sus requisiciones antes de cerrar.',
  RECEPCION_YA_RECIBIDA: 'La orden de compra ya tiene una recepción. Revisa lo recibido antes de registrar otra e indica el motivo si corresponde.',
  DICTAMEN_RECHAZO_SIN_EVIDENCIA: 'Falta la evidencia del material rechazado. Adjunta una foto o documento en la recepción para continuar.',
  PROVEEDOR_REGIMEN_REQUERIDO: 'Elige el régimen fiscal del proveedor nacional.',
  PROVEEDOR_REGIMEN_NO_EXISTE: 'El régimen fiscal no está en el catálogo. Elige uno vigente.',
  PROVEEDOR_REGIMEN_INVALIDO: 'El régimen fiscal no corresponde al proveedor. Revisa su constancia fiscal y corrige la selección.',
  PROVEEDOR_REGIMEN_NO_APLICA: 'Un proveedor extranjero no lleva régimen fiscal del SAT. Quita el régimen para continuar.',
};

/** Presentación de errores: nunca publica excepciones ni detalles internos del servidor. */
export function errorUsuario(error: unknown, contexto = 'No se pudo completar la acción.'): string {
  if (esApiError(error)) {
    const motivo = MOTIVOS[error.code ?? ''];
    if (motivo) return motivo;
    if (error.status === 401) return 'La sesión venció. Inicia sesión de nuevo para continuar.';
    if (error.status === 403) return 'Tu usuario no tiene permiso para esta acción. Pide el acceso al administrador.';
    if (error.code === 'CONCURRENCY_CONFLICT' || error.status === 412 || error.status === 428) {
      return 'El registro cambió o la acción ya se procesó. Recarga y revisa su estado antes de continuar.';
    }
    if (error.status === 400 || error.status === 409 || error.status === 422) {
      const detalle = error.problem.detail?.trim();
      const interno = /[A-Z][A-Z0-9]*_[A-Z0-9_]+|\b(?:HTTP|SQL|endpoint|payload|backend|exception|stack|agregado|bucket|override|snapshot|ingesta|dedupe|listener|idempotente|NoOp|stub|master|ADR-|86e3)\b|https?:|\.(?:cs|tsx?|md)\b/i;
      if (detalle && !/[a-z][A-Z][a-z]/.test(detalle) && !interno.test(detalle)) {
        if (/^(Adjunta|Captura|Elige|Indica|Revisa|Selecciona|Completa|Corrige|Registra|Vincula|Espera|Inicia|Pide|Contacta)\b/i.test(detalle)) return detalle;
        const mensaje = /[.!?]$/.test(detalle) ? detalle : `${detalle}.`;
        return `${mensaje} Revisa los datos y resuelve lo pendiente antes de continuar.`;
      }
      return `${contexto} Revisa los campos marcados y el estado del registro antes de continuar.`;
    }
  }
  return `${contexto} Revisa tu conexión y vuelve a cargar el registro; si continúa, avisa a soporte.`;
}

/** Conserva el estado necesario para «no encontrado», sin texto técnico en las vistas de error. */
export function problemaUsuario(error: unknown) {
  return {
    type: 'about:blank',
    status: esApiError(error) ? error.status : 0,
    title: 'No se pudo cargar la información',
    detail: errorUsuario(error),
  };
}

/** Adapta los reportes existentes, que reciben ApiError para su estado de carga. */
export function errorDeReporte(error: unknown): ApiError | undefined {
  return esApiError(error) ? new ApiError(problemaUsuario(error), error.status) : undefined;
}
