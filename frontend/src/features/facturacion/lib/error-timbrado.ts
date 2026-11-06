import { esApiError } from '@/lib/api';

export function mensajeErrorTimbrado(error: unknown): string {
  const generico = 'No se pudo completar el timbrado. Revisa el estado del comprobante con Facturación antes de reintentar.';
  if (esApiError(error)) {
    if (error.code === 'CONFIG_PAC_NO_DISPONIBLE') {
      return 'No hay timbrado configurado para esta empresa. Pide a un administrador que lo active en Administración → Integraciones fiscales.';
    }
    const campos = [
      ...(error.problem.errores ?? []).map((e) => e.mensaje),
      ...Object.values(error.problem.errors ?? {}).flat(),
    ].filter(Boolean);
    if (error.status >= 500 && campos.length === 0) return generico;
    const causa =
      [...new Set([error.problem.detail?.trim(), ...campos].filter(Boolean))].join(' ') ||
      error.problem.title?.trim() ||
      generico;
    return /\b(?:[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+|HTTP\s*\d{3}|SQLSTATE|payload|endpoint|DTO|JSON|\w*Exception)\b/i.test(causa) ? generico : causa;
  }
  return 'No se pudo completar el timbrado. Revisa tu conexión e intenta de nuevo.';
}
