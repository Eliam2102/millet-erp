import { useMemo, useState } from 'react';
import { toast } from 'sonner';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  useLigarPagoACuenta,
  usePasivosPendientes,
} from '@/features/tesoreria/api/useTesoreria';
import type { PagoACuentaAbiertoResponse } from '@/features/tesoreria/api/types';
import { formatoFecha, formatoMonto } from '@/features/tesoreria/lib/formato';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';

export interface LigarPagoACuentaDialogProps {
  pago: PagoACuentaAbiertoResponse | null;
  onOpenChange: (open: boolean) => void;
}

/**
 * Dialog de liga tardía (TES-FE-PR4, §4.3): aplica un pago a cuenta
 * abierto a un pasivo del MISMO proveedor sin re-desembolsar (emite
 * <c>aplicado.v1</c>). Si el importe difiere del restante del movimiento
 * o del saldo del pasivo, se muestra la comparación y se exige
 * confirmación explícita (doble confirmación del §3.4 — la matriz de
 * suplencias llega con T-G4).
 */
export function LigarPagoACuentaDialog({
  pago,
  onOpenChange,
}: LigarPagoACuentaDialogProps) {
  const ligar = useLigarPagoACuenta();
  const keyFor = useBodyScopedIdempotencyKey();

  const pasivos = usePasivosPendientes({
    proveedorId: pago?.proveedorId ?? undefined,
    soloConSaldo: true,
    limit: 100,
  });

  const [facturaId, setFacturaId] = useState<string | null>(null);
  const [importe, setImporte] = useState('');
  const [confirmaDiferencia, setConfirmaDiferencia] = useState(false);

  const restante = pago == null ? 0 : pago.monto - pago.importeLigado;
  const pasivo = useMemo(
    () =>
      (pasivos.data?.items ?? []).find(
        (p) => p.facturaProveedorId === facturaId,
      ) ?? null,
    [pasivos.data, facturaId],
  );

  const importeNum = Number(importe);
  const maximo = pasivo == null ? restante : Math.min(restante, pasivo.saldoPendiente);
  const importeValido =
    Number.isFinite(importeNum) && importeNum > 0 && importeNum <= maximo;
  const difiere =
    pasivo != null && importeValido && importeNum !== pasivo.saldoPendiente;

  function seleccionarPasivo(id: string) {
    setFacturaId(id);
    const p = (pasivos.data?.items ?? []).find((x) => x.facturaProveedorId === id);
    if (p != null) {
      setImporte(String(Math.min(restante, p.saldoPendiente)));
    }
    setConfirmaDiferencia(false);
  }

  function cerrar(next: boolean) {
    if (!next) {
      setFacturaId(null);
      setImporte('');
      setConfirmaDiferencia(false);
    }
    onOpenChange(next);
  }

  function confirmar() {
    if (pago == null || facturaId == null || !importeValido) return;
    if (difiere && !confirmaDiferencia) {
      setConfirmaDiferencia(true);
      return;
    }
    const command = {
      movimientoId: pago.movimientoId,
      facturaProveedorId: facturaId,
      importe: importeNum,
    };
    ligar.mutate(
      {
        ...command,
        // Reintento tras timeout no aplica el pago a cuenta dos veces.
        idempotencyKey: keyFor(command),
      },
      {
        onSuccess: () => {
          toast.success('Pago a cuenta ligado', {
            description:
              'Se aplicó sin re-desembolso; CxP recibirá aplicado.v1 por la factura.',
          });
          cerrar(false);
        },
        onError: (error) => {
          toast.error(
            esApiError(error) ? error.problem.title : 'No se pudo ligar el pago',
            {
              description: esApiError(error)
                ? (error.problem.detail ?? `Código: ${error.traceId}`)
                : undefined,
            },
          );
        },
      },
    );
  }

  return (
    <Dialog open={pago != null} onOpenChange={cerrar}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Ligar pago a cuenta al pasivo</DialogTitle>
          <DialogDescription>
            {pago != null &&
              `Movimiento de ${formatoMonto(pago.monto, pago.moneda)} del ${formatoFecha(pago.fechaValor)} — restante por ligar ${formatoMonto(restante, pago.moneda)}. Sin re-desembolso.`}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3">
          <div className="space-y-1">
            <label className="text-xs text-muted-foreground">
              Pasivo autorizado del proveedor
            </label>
            <Select value={facturaId ?? ''} onValueChange={seleccionarPasivo}>
              <SelectTrigger aria-label="Pasivo a ligar">
                <SelectValue
                  placeholder={
                    pasivos.isLoading
                      ? 'Cargando pasivos…'
                      : (pasivos.data?.items.length ?? 0) === 0
                        ? 'El proveedor no tiene pasivos autorizados'
                        : 'Selecciona el pasivo'
                  }
                />
              </SelectTrigger>
              <SelectContent>
                {(pasivos.data?.items ?? []).map((p) => (
                  <SelectItem
                    key={p.facturaProveedorId}
                    value={p.facturaProveedorId}
                  >
                    <span className="font-mono text-xs">
                      {p.folioProveedor ?? p.facturaProveedorId.slice(0, 8)}
                    </span>{' '}
                    · {formatoMonto(p.saldoPendiente, p.moneda)} · vence{' '}
                    {formatoFecha(p.fechaVencimiento)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1">
            <label className="text-xs text-muted-foreground" htmlFor="liga-importe">
              Importe a aplicar
            </label>
            <Input
              id="liga-importe"
              type="number"
              inputMode="decimal"
              min={0.01}
              max={maximo}
              step="0.01"
              value={importe}
              onChange={(e) => {
                setImporte(e.target.value);
                setConfirmaDiferencia(false);
              }}
              className="text-right font-mono"
              disabled={facturaId == null}
            />
          </div>

          {difiere && (
            <div className="rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800">
              El importe ({pago != null ? formatoMonto(importeNum, pago.moneda) : ''})
              difiere del saldo del pasivo (
              {pasivo != null ? formatoMonto(pasivo.saldoPendiente, pasivo.moneda) : ''}
              ). La diferencia por comisiones se resuelve en conciliación, no
              aquí — confirma solo si es correcto (§3.4).
            </div>
          )}
        </div>

        <DialogFooter>
          <Button variant="ghost" onClick={() => cerrar(false)} disabled={ligar.isPending}>
            Cancelar
          </Button>
          <Button
            onClick={confirmar}
            disabled={ligar.isPending || facturaId == null || !importeValido}
            variant={difiere && !confirmaDiferencia ? 'destructive' : 'default'}
          >
            {ligar.isPending
              ? 'Ligando…'
              : difiere && !confirmaDiferencia
                ? 'Revisar diferencia'
                : 'Ligar'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
