export const ESTADOS_PERMISO = {
  heredado: { texto: 'Heredado del rol', tono: 'neutral' },
  concedido: { texto: 'Personalizado: añadido', tono: 'success' },
  denegado: { texto: 'Personalizado: quitado', tono: 'danger' },
} as const;
export const ESTADOS_CUENTA = {
  activo: { texto: 'Activo', tono: 'success' },
  inactivo: { texto: 'Inactivo', tono: 'neutral' },
  sistema: { texto: 'Sistema', tono: 'info' },
} as const;
export const OPCIONES_CUENTA = [
  { valor: 1, label: 'Activo' },
  { valor: 0, label: 'Inactivo' },
];
