import { formatearFechaCelda } from '@/components/erp/reportes/formatear-fecha-celda';

export function renderCelda(valor: unknown, tipo: string, decimales?: number | null): string {
  if (valor == null) return '—';
  switch (tipo) {
    case 'moneda':
      return typeof valor === 'number'
        ? new Intl.NumberFormat('es-MX', {
            style: 'currency',
            currency: 'MXN',
            minimumFractionDigits: 2,
          }).format(valor)
        : String(valor);
    case 'numero':
      return typeof valor === 'number'
        ? valor.toLocaleString('es-MX', {
            minimumFractionDigits: decimales ?? 0,
            maximumFractionDigits: decimales ?? 4,
          })
        : String(valor);
    case 'fecha':
      return typeof valor === 'string' ? formatearFechaCelda(valor) : String(valor);
    default:
      return String(valor);
  }
}
