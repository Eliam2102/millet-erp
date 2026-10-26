import { createElement } from 'react';
import { toast } from 'sonner';
import { ReceptorIncompletoBanner } from '../components/ReceptorFiscalInfo';
import { parseReceptorFiscalError } from './receptor-fiscal-error';

export function mostrarErrorReceptorFiscal(error: unknown): boolean {
  const diagnostico = parseReceptorFiscalError(error);
  if (!diagnostico) return false;
  toast.error('No se puede timbrar', {
    description: createElement(ReceptorIncompletoBanner, {
      clienteId: diagnostico.clienteId ?? null,
      puedeCorregirEnCatalogo: false,
      diagnostico,
    }),
    duration: Infinity,
    closeButton: true,
  });
  return true;
}
