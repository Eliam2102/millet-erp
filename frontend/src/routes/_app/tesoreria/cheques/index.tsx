import { createFileRoute } from '@tanstack/react-router';
import { BandejaChequesRecibidos } from '@/features/tesoreria/pages/BandejaChequesRecibidos';

/** M-372: cheques recibidos (en firme pendiente → disponible / rebotado). */
export const Route = createFileRoute('/_app/tesoreria/cheques/')({
  component: BandejaChequesRecibidos,
});
