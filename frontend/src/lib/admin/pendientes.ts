import { adminRegistry, type AdminSection } from './registry';
import type {
  ListarParametrosResponse,
  ListarSeriesResponse,
} from '@/modules/administracion/api/types';

export interface EquivalenciaResumen {
  observaciones: string | null;
}
export interface PendienteAdmin {
  entry: AdminSection;
  estado: 'Por validar' | 'Datos de ejemplo';
}

/** Solo respuestas completas del API; ausencia/error de consulta no equivale a pendiente. */
export function pendientesComprobados(datos: {
  equivalencias?: readonly EquivalenciaResumen[];
  series?: ListarSeriesResponse;
  parametros?: ListarParametrosResponse;
}): PendienteAdmin[] {
  const result: PendienteAdmin[] = [];
  function add(
    href: string,
    titulo: string,
    descripcion: string,
    estado: PendienteAdmin['estado'],
  ) {
    const entry = adminRegistry.find((row) => row.href === href);
    if (entry) result.push({ entry: { ...entry, titulo, descripcion }, estado });
  }
  if (datos.equivalencias?.some((row) => /\bDEMO\b/i.test(row.observaciones ?? ''))) {
    add(
      '/centros-costo/configuracion',
      'Centro de costo por departamento',
      'Hay equivalencias marcadas como ejemplo en la sucursal seleccionada.',
      'Datos de ejemplo',
    );
  }
  if (datos.series?.total === 0) {
    add(
      '/admin/series',
      'Series y folios de la sucursal',
      'No hay series propias registradas para la sucursal seleccionada.',
      'Por validar',
    );
  }
  if (
    datos.parametros &&
    !datos.parametros.items.some(
      (row) => row.clave === 'system.dias-festivos' && row.valor.trim().length > 0,
    )
  ) {
    add(
      '/admin/parametros',
      'Días festivos',
      'No hay un calendario de días festivos capturado.',
      'Por validar',
    );
  }
  return result;
}
