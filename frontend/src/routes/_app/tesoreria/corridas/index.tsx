import { createFileRoute } from '@tanstack/react-router';
import { ClipboardList } from 'lucide-react';
import { BandejaPlaceholder } from '@/features/tesoreria/components/BandejaPlaceholder';

/** Corridas de pago (P1+P3) — pantalla real en TES-FE-PR3 (backend TES-PR5, gate T-G4). */
export const Route = createFileRoute('/_app/tesoreria/corridas/')({
  component: () => (
    <BandejaPlaceholder
      titulo="Corridas de pago"
      descripcion="Lotes de pasivos autorizables como unidad, con oficio de cartera."
      icono={ClipboardList}
      mensajeArranque="Las corridas agrupan pasivos de la bandeja para autorizarse vía la matriz (Borrador → En autorización → Autorizada → Ejecutada → Cerrada)."
      proximamente="Llega con TES-FE-PR3 (requiere la matriz de autorización — gate T-G4)"
    />
  ),
});
