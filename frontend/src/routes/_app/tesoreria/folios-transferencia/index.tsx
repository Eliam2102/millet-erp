import { createFileRoute } from '@tanstack/react-router';
import { BandejaFoliosTransferencia } from '@/features/tesoreria/pages/BandejaFoliosTransferencia';

/** Folios de confirmación de transferencias (F6.6): Ingresos los emite; Caja los aplica en la forma 03. */
export const Route = createFileRoute('/_app/tesoreria/folios-transferencia/')({
  component: BandejaFoliosTransferencia,
});
