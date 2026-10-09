import { z } from 'zod';
import type { CuentaBody } from '../api/types';

/** Espejo del backend: código ≤30 (patrón real lo valida el servidor), nombre ≤254, agrupador ≤30, grupo ≤60. */
export const CuentaSchema = z.object({
  codigo: z.string().trim().min(1, 'El código es requerido.').max(30, 'Máximo 30 caracteres.'),
  nombre: z.string().trim().min(1, 'El nombre es requerido.').max(254, 'Máximo 254 caracteres.'),
  padreId: z.string(), // '' = sin padre (raíz)
  naturaleza: z.enum(['', 'Deudora', 'Acreedora']), // '' = pendiente de validación
  noAfectableManual: z.boolean(),
  cuentaControl: z.enum(['Ninguna', 'Clientes', 'Deudores', 'Proveedores', 'Acreedores']),
  rubroId: z.string(), // '' = sin rubro (solo aplica a cuentas de nivel 1)
  codigoAgrupador: z.string().trim().max(30, 'Máximo 30 caracteres.'),
  grupoReporte: z.string().trim().max(60, 'Máximo 60 caracteres.'),
});

export type CuentaValues = z.infer<typeof CuentaSchema>;

export const VALORES_VACIOS: CuentaValues = {
  codigo: '',
  nombre: '',
  padreId: '',
  naturaleza: '',
  cuentaControl: 'Ninguna',
  noAfectableManual: false,
  rubroId: '',
  codigoAgrupador: '',
  grupoReporte: '',
};

/**
 * Vacío = null: una naturaleza nula queda «pendiente de validación»; nunca se supone un valor. El tipo no se envía:
 * lo calcula el sistema por la jerarquía (P19). El rubro solo aplica a cuentas sin padre.
 */
export function aBody(v: CuentaValues, conCodigo: boolean): CuentaBody {
  return {
    ...(conCodigo ? { codigo: v.codigo.trim() } : {}),
    nombre: v.nombre.trim(),
    padreId: v.padreId || null,
    naturaleza: v.naturaleza || null,
    tipo: null,
    cuentaControl: v.cuentaControl,
    noAfectableManual: v.noAfectableManual,
    codigoAgrupador: v.codigoAgrupador.trim() || null,
    grupoReporte: v.grupoReporte.trim() || null,
    rubroId: v.padreId ? null : v.rubroId || null,
  };
}
