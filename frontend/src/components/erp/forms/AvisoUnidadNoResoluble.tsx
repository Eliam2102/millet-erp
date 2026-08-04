import { AlertTriangle } from 'lucide-react';
import { MENSAJE_UNIDAD_NO_RESOLUBLE } from '@/components/erp/forms/decimales-filas';

/**
 * <c>&lt;AvisoUnidadNoResoluble/&gt;</c> — advertencia tenue (no bloqueante) para
 * las capturas de Almacén cuando la unidad de una línea no matchea el catálogo,
 * así el advisory de decimales no puede evaluarla (ADR-0046). El servidor la
 * valida al guardar; por eso NO impide la captura. Sustituye al skip silencioso
 * previo. Reutilizado por recepción, salida-vs-RQ y devolución a proveedor.
 */
export function AvisoUnidadNoResoluble({ visible }: { visible: boolean }) {
  if (!visible) return null;
  return (
    <p className="mt-1 flex items-center gap-1 text-[11px] text-amber-600">
      <AlertTriangle className="h-3 w-3 shrink-0" aria-hidden="true" />
      {MENSAJE_UNIDAD_NO_RESOLUBLE}
    </p>
  );
}
