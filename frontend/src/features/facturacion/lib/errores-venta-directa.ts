import type { ApiError } from '@/lib/api/error';
import type { FormConSetError } from '@/lib/api/apply-server-errors';

/**
 * F6.1 — errores de la clasificación de pedidos manuales (venta directa /
 * emergencia) y de su emisión con la serie de Venta directa.
 *
 * <para>El pipeline de validación del backend emite <c>errores[].campo</c>
 * con el <c>PropertyName</c> de FluentValidation (PascalCase, p. ej.
 * <c>ClaseManual</c>), así que el campo del form se resuelve por
 * <c>codigo</c> y, si no se conoce, normalizando la primera letra.</para>
 */
export const CAMPO_POR_CODIGO_CLASIFICACION: Record<string, string> = {
  PEDIDO_MANUAL_CLASE_REQUERIDA: 'claseManual',
  PEDIDO_EMERGENCIA_SIN_PERMISO: 'claseManual',
  PEDIDO_VD_SUBTIPO_REQUERIDO: 'subtipoVentaDirecta',
  PEDIDO_EMERGENCIA_SIN_SUBTIPO: 'subtipoVentaDirecta',
  PEDIDO_EMERGENCIA_MOTIVO_REQUERIDO: 'motivoEmergencia',
  PEDIDO_ACTIVO_COMPORTAMIENTO: 'comportamientoFiscal',
};

function normalizarCampo(campo: string): string {
  const limpio = campo.replace(/^\$\./, '');
  return limpio.length > 0 ? limpio[0].toLowerCase() + limpio.slice(1) : limpio;
}

/**
 * Asigna al form los errores del POST/PUT de pedido manual. Devuelve
 * <c>true</c> si mapeó algo (el caller no muestra toast genérico).
 */
export function aplicarErroresPedidoManual(
  form: FormConSetError,
  error: ApiError,
): boolean {
  // 403 de negocio (sin errores[]): emergencia sin permiso.
  const campoCodigo =
    error.code != null ? CAMPO_POR_CODIGO_CLASIFICACION[error.code] : undefined;
  if (campoCodigo != null && (error.problem.errores?.length ?? 0) === 0) {
    form.setError(
      campoCodigo,
      {
        type: error.code as string,
        message:
          error.code === 'PEDIDO_EMERGENCIA_SIN_PERMISO'
            ? 'No tienes permiso para capturar pedidos de emergencia.'
            : (error.problem.detail ?? error.problem.title),
      },
      { shouldFocus: true },
    );
    return true;
  }

  const errores = error.problem.errores;
  if (errores == null || errores.length === 0) return false;
  let didFocus = false;
  for (const item of errores) {
    const campo =
      CAMPO_POR_CODIGO_CLASIFICACION[item.codigo] ?? normalizarCampo(item.campo);
    if (campo.length === 0) continue;
    form.setError(
      campo,
      { type: item.codigo, message: item.mensaje },
      { shouldFocus: !didFocus },
    );
    didFocus = true;
  }
  return didFocus;
}

/** Destino del enlace de destrabe de un error de emisión de venta directa. */
export type DestinoErrorVentaDirecta = 'series' | 'editar-pedido' | null;

export interface ErrorEmisionVentaDirecta {
  titulo: string;
  mensaje: string;
  destino: DestinoErrorVentaDirecta;
}

/**
 * Traduce los 422 de la emisión desde un pedido de venta directa /
 * emergencia a un mensaje claro con su vía de destrabe. <c>null</c> si el
 * error no es de este flujo.
 */
export function errorEmisionVentaDirecta(
  error: unknown,
): ErrorEmisionVentaDirecta | null {
  const code =
    typeof error === 'object' && error != null && 'code' in error
      ? (error as { code?: unknown }).code
      : undefined;
  switch (code) {
    case 'SERIE_VENTA_DIRECTA_NO_CONFIGURADA':
      return {
        titulo: 'Falta la serie de Venta directa',
        mensaje:
          'La sucursal del pedido no tiene una serie activa de Venta directa. Configúrala en Administración → Series y vuelve a emitir.',
        destino: 'series',
      };
    case 'SERIE_VD_REQUIERE_SUCURSAL':
      return {
        titulo: 'Serie de Venta directa sin sucursal',
        mensaje:
          'La serie de Venta directa se configura por sucursal. Revisa la serie en Administración → Series.',
        destino: 'series',
      };
    case 'PEDIDO_MANUAL_SIN_CLASIFICAR':
      return {
        titulo: 'Pedido manual sin clase',
        mensaje:
          'Este pedido manual no indica si es venta directa o emergencia. Edítalo para clasificarlo y después factúralo.',
        destino: 'editar-pedido',
      };
    case 'FAC_PEDIDO_ACTIVO_COMPORTAMIENTO':
      return {
        titulo: 'Comportamiento fiscal no coincide',
        mensaje:
          'La venta directa de un activo se factura con el comportamiento «Venta de activo fijo» (y solo ella). Corrige el pedido o el comportamiento fiscal.',
        destino: 'editar-pedido',
      };
    default:
      return null;
  }
}
