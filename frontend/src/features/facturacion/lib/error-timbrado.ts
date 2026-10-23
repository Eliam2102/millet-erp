import { esApiError } from '@/lib/api';

export function mensajeErrorTimbrado(error: unknown): string {
  if (esApiError(error)) {
    if (error.code === 'CONFIG_PAC_NO_DISPONIBLE') {
      return 'No hay timbrado configurado para esta empresa. Pide a un administrador que lo active en Administración → Integraciones fiscales.';
    }
    const campos = [
      ...(error.problem.errores ?? []).map((e) => e.mensaje),
      ...Object.values(error.problem.errors ?? {}).flat(),
    ].filter(Boolean);
    const causa =
      [...new Set([error.problem.detail?.trim(), ...campos].filter(Boolean))].join(' ') ||
      error.problem.title?.trim() ||
      'El servidor no informó la causa del rechazo.';
    return causa;
  }
  return 'No se pudo completar el timbrado. Revisa tu conexión e intenta de nuevo.';
}
