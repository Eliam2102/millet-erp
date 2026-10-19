import { useId, useState } from 'react';
import { Loader2 } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { useSolicitarReembolsoNc } from '@/features/facturacion/api/useFacturas';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';

/** Tope del backend (SolicitarReembolsoNotaCreditoValidator: MaximumLength(400)). */
export const MOTIVO_REEMBOLSO_MAX = 400;

interface SolicitarReembolsoNcDialogProps {
  notaCreditoId: string;
  folio: string;
  total: number;
  moneda: string;
  onCerrar: () => void;
}

/**
 * Diálogo para pedir a Tesorería el reembolso en dinero de una NC timbrada
 * (T9.6 · CA6.17). El importe no puede exceder el total de la nota; el saldo de
 * la factura no cambia ([Decisión 13-K]).
 */
export function SolicitarReembolsoNcDialog({
  notaCreditoId,
  folio,
  total,
  moneda,
  onCerrar,
}: SolicitarReembolsoNcDialogProps) {
  const id = useId();
  const [importe, setImporte] = useState(total.toFixed(2));
  const [motivo, setMotivo] = useState('');
  const keyFor = useBodyScopedIdempotencyKey();
  const solicitar = useSolicitarReembolsoNc();

  const importeNum = Number(importe);
  const motivoLimpio = motivo.trim();
  const valido =
    importeNum > 0 && importeNum <= total && motivoLimpio.length > 0 && motivoLimpio.length <= MOTIVO_REEMBOLSO_MAX;

  function confirmar() {
    if (!valido || solicitar.isPending) return;
    const args = { notaCreditoId, importe: importeNum, motivo: motivoLimpio };
    solicitar.mutate(
      { ...args, idempotencyKey: keyFor(args) },
      {
        onSuccess: () => {
          toast.success('Reembolso solicitado a Tesorería');
          onCerrar();
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, { description: error.problem.detail });
          } else {
            toast.error('No se pudo solicitar el reembolso.');
          }
        },
      },
    );
  }

  return (
    <Dialog open onOpenChange={(abierto) => !abierto && !solicitar.isPending && onCerrar()}>
      <DialogContent closeLabel="Cerrar" data-component="solicitar-reembolso-nc-dialog">
        <DialogHeader>
          <DialogTitle>Solicitar reembolso de la nota de crédito</DialogTitle>
          <DialogDescription>
            <span className="font-mono">{folio}</span> · total{' '}
            <span className="tabular-nums">
              {total.toFixed(2)} {moneda}
            </span>
            . Tesorería recibirá la solicitud, capturará la cuenta del cliente y la autorizará antes
            de pagar. El saldo de la factura no cambia.
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-1.5">
          <Label htmlFor={`${id}-importe`} className="text-xs font-medium text-ink-strong">
            Importe a reembolsar
          </Label>
          <Input
            id={`${id}-importe`}
            type="number"
            step="0.01"
            min="0.01"
            max={total}
            className="tabular-nums"
            value={importe}
            onChange={(e) => setImporte(e.target.value)}
            aria-invalid={importe !== '' && !(importeNum > 0 && importeNum <= total)}
            disabled={solicitar.isPending}
          />
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor={`${id}-motivo`} className="text-xs font-medium text-ink-strong">
            Motivo
          </Label>
          <Textarea
            id={`${id}-motivo`}
            rows={3}
            value={motivo}
            maxLength={MOTIVO_REEMBOLSO_MAX}
            onChange={(e) => setMotivo(e.target.value)}
            disabled={solicitar.isPending}
          />
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={onCerrar} disabled={solicitar.isPending}>
            Volver
          </Button>
          <Button onClick={confirmar} disabled={!valido || solicitar.isPending}>
            {solicitar.isPending && <Loader2 className="animate-spin" />}
            Solicitar reembolso
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
