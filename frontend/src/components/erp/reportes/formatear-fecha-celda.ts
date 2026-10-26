/** Una fecha de calendario (yyyy-MM-dd) no es un instante: no se convierte desde UTC. */
export function formatearFechaCelda(valor: string): string {
  return /^\d{4}-\d{2}-\d{2}$/.test(valor)
    ? new Date(`${valor}T00:00:00Z`).toLocaleDateString('es-MX', { timeZone: 'UTC' })
    : new Date(valor).toLocaleDateString('es-MX');
}
