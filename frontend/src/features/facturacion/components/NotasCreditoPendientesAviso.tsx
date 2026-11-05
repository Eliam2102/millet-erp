import type { NotaCreditoPendienteDetalle } from '@/features/facturacion/api/types';
import { ETIQUETA_MOTIVO_NC } from '@/features/facturacion/lib/glosario';
import { TriangleAlert } from 'lucide-react';
import { TimbradoFallidoBanner } from './TimbradoFallidoBanner';

/**
 * U-1 (86e3yxn2p): aviso «nota de crédito pendiente» en el detalle de la factura.
 * La factura timbrada se guardó aunque su NC de ranura o de anticipo no timbró;
 * mientras la NC no quede timbrada no acredita (el por cobrar es la factura
 * completa). Cada NC trae su motivo y el reintento de timbrado (o descarte).
 */
export function NotasCreditoPendientesAviso({ notas }: { notas: NotaCreditoPendienteDetalle[] }) {
  if (notas.length === 0) return null;
  return (
    <section className="space-y-2" aria-label="Nota de crédito pendiente" data-testid="nc-pendiente">
      <p
        role="note"
        className="flex items-start gap-1.5 rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg"
      >
        <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} />
        <span>
          <span className="font-medium">Nota de crédito pendiente.</span> La factura está timbrada, pero{' '}
          {notas.length === 1 ? 'su nota de crédito no se timbró' : `${notas.length} notas de crédito no se timbraron`}
          . Hasta timbrarla, el saldo del cliente es la factura completa. No vuelvas a facturar: reintenta la
          nota.
        </span>
      </p>
      {notas.map((nc) => (
        <div key={nc.id} className="space-y-1">
          <p className="text-sm">
            <span className="font-mono text-xs">{nc.folio}</span> ·{' '}
            {ETIQUETA_MOTIVO_NC[nc.motivo] ?? nc.motivo} ·{' '}
            <span className="font-mono tabular-nums">{nc.total.toFixed(2)}</span>
            {nc.estado === 'TimbradoEnProceso' && ' · en proceso con el PAC'}
          </p>
          <TimbradoFallidoBanner
            comprobanteId={nc.id}
            estado={nc.estado}
            errorCodigo={nc.timbradoErrorCodigo}
            errorMensaje={nc.timbradoErrorMensaje}
            esNotaCreditoPendiente
          />
        </div>
      ))}
    </section>
  );
}
