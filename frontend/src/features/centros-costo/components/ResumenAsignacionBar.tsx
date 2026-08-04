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
    <div
      className="flex flex-wrap items-center gap-x-4 gap-y-1 rounded-md border bg-muted/30 px-3 py-2 text-sm"
      data-testid="resumen-asignacion"
    >
      <span>
        <span className="font-medium">{d1}:</span> {resumen.dim1Completas}/
        {resumen.dim1Vivas} completas
      </span>
      <span className="text-muted-foreground">·</span>
      <span>
        <span className="font-medium">{d2}:</span> {resumen.dim2Completas}/
        {resumen.dim2Vivas} completas
      </span>
      <span className="text-muted-foreground">·</span>
      <span>
        <span className="font-medium">{d3}:</span> {resumen.dim3Asignadas}/
        {resumen.dim3Vivas} asignadas
      </span>
    </div>
  );
}
