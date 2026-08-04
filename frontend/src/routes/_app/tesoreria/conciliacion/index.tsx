import { createFileRoute } from '@tanstack/react-router';
import { Scale } from 'lucide-react';
import { BandejaPlaceholder } from '@/features/tesoreria/components/BandejaPlaceholder';

/** Conciliación bancaria (P1 + matching) — pantalla real en TES-FE-PR5 (backend TES-PR9, gates T-G2/T-G8). */
export const Route = createFileRoute('/_app/tesoreria/conciliacion/')({
  component: () => (
    <BandejaPlaceholder
      titulo="Conciliación bancaria"
      descripcion="Cruce mensual extracto ↔ movimientos internos por cuenta, con matching asistido y acta."
      icono={Scale}
      mensajeArranque="Las conciliaciones se abren por cuenta y período cargando el estado de cuenta del banco (perfil por banco); el cierre exige saldo cuadrado (RN-7)."
      proximamente="Llega con TES-FE-PR5 (requiere formatos de extracto — gates T-G2/T-G8)"
    />
  ),
});
