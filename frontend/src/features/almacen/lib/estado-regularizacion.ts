export function estadoRegularizacion(salida: { pendienteRegularizacion: boolean; vencido: boolean }) {
  if (!salida.pendienteRegularizacion) return { texto: 'Regularizado', variant: 'success' } as const;
  return salida.vencido
    ? { texto: 'Vencido', variant: 'danger' } as const
    : { texto: 'Por regularizar', variant: 'warning' } as const;
}
