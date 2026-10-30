import { createFileRoute } from '@tanstack/react-router';
import { NuevaCorrida } from '@/features/tesoreria/pages/NuevaCorrida';

/** Nueva corrida de pagos (T9.1-A): cuenta, pasivos elegibles y bruto/retenciones/neto por proveedor. */
export const Route = createFileRoute('/_app/tesoreria/corridas/nueva')({
  component: NuevaCorrida,
});
