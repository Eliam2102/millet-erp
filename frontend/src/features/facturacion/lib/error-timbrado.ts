import { esApiError } from '@/lib/api';

export function mensajeErrorTimbrado(error: unknown): string {
  if (esApiError(error)) {
    if (error.code === 'CONFIG_PAC_NO_DISPONIBLE') {
      return 'No hay timbrado configurado para esta empresa. Pide a un administrador que lo active en Administración → Integraciones fiscales.';
    }
    return error.problem.detail?.trim() || error.problem.title?.trim() ||
      'No se pudo completar el timbrado. Intenta de nuevo.';
  }
  return 'No se pudo completar el timbrado. Revisa tu conexión e intenta de nuevo.';
}
