import { KpiCard } from '@/components/erp/display/KpiCard';
import { etiquetaNivel } from '@/features/centros-costo/lib/etiquetas';
import type { ResumenAsignacion } from '@/features/centros-costo/api/types';

/**
 * Barra resumen por dimensión (05 §4.2): "Dimensión 1: 2/5 completas ·
 * Dimensión 3: 118/361 asignadas". Los conteos vienen del backend
 * (`ResumenAsignacion`); vocabulario vía el helper único.
 */
export function ResumenAsignacionBar({ resumen }: { resumen: ResumenAsignacion }) {
  const d1 = etiquetaNivel('dim1', 'asignacion');
  const d2 = etiquetaNivel('dim2', 'asignacion');
  const d3 = etiquetaNivel('dim3', 'asignacion');

  return (
    <div className="grid gap-3 sm:grid-cols-3" data-testid="resumen-asignacion">
      <KpiCard
        etiqueta={d1}
        valor={`${resumen.dim1Completas}/${resumen.dim1Vivas}`}
        subdato="completas"
      />
      <KpiCard
        etiqueta={d2}
        valor={`${resumen.dim2Completas}/${resumen.dim2Vivas}`}
        subdato="completas"
      />
      <KpiCard
        etiqueta={d3}
        valor={`${resumen.dim3Asignadas}/${resumen.dim3Vivas}`}
        subdato="asignadas"
      />
    </div>
  );
}
