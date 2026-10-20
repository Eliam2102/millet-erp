import { EstatusCatalogo } from '../api/types';
export const ESTADOS_CATALOGO = {
  [EstatusCatalogo.Activo]: { texto: 'Activo', tono: 'success' },
  [EstatusCatalogo.Inactivo]: { texto: 'Inactivo', tono: 'neutral' },
  [EstatusCatalogo.EnRevision]: { texto: 'En revisión', tono: 'warning' },
} as const;
export const OPCIONES_CATALOGO = Object.entries(ESTADOS_CATALOGO).map(([valor, estado]) => ({
  valor: Number(valor),
  label: estado.texto,
}));
export const ESTADOS_ACCESO = {
  activo: { texto: 'Acceso activo', tono: 'success' },
  pendiente: { texto: 'Sin iniciar sesión', tono: 'warning' },
  provision: { texto: 'En provisión', tono: 'info' },
  error: { texto: 'Error de provisión', tono: 'danger' },
  bloqueado: { texto: 'Acceso bloqueado', tono: 'danger' },
  sinAcceso: { texto: 'Sin acceso ERP', tono: 'neutral' },
} as const;
export const ESTADOS_CSD = {
  Vigente: { texto: 'Vigente', variante: 'success' },
  ProximoAVencer: { texto: 'Próximo a vencer', variante: 'warning' },
  Vencido: { texto: 'Vencido', variante: 'danger' },
} as const;
