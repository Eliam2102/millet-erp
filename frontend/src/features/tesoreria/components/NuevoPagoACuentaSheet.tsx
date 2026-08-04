import { hoyLocalISO } from '@/lib/datetime';
import { useState } from 'react';
import { toast } from 'sonner';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { ProveedorSelector } from '@/components/erp/selectors/ProveedorSelector';
import { CuentaBancariaSelector } from './CuentaBancariaSelector';
import { useRegistrarPagoACuenta } from '@/features/tesoreria/api/useTesoreria';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';

export interface NuevoPagoACuentaSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Sheet "Nuevo pago a cuenta" (TES-FE-PR4, §3.4 / TES-2): el pago
 * urgente sin factura SÍ se registra — motivo obligatorio, proveedor
 * opcional (si se conoce, entra al gate RN-2: máximo uno abierto por
 * proveedor; el backend responde 422 <c>PAGO_CUENTA_ABIERTO_EXISTENTE</c>).
 */
export function NuevoPagoACuentaSheet({
  open,
  onOpenChange,
}: NuevoPagoACuentaSheetProps) {
  const registrar = useRegistrarPagoACuenta();
  const keyFor = useBodyScopedIdempotencyKey();

  const [proveedorId, setProveedorId] = useState<string | null>(null);
  const [cuentaId, setCuentaId] = useState<string | null>(null);
  const [monto, setMonto] = useState('');
  const [fechaValor, setFechaValor] = useState(() =>
    hoyLocalISO(),
  );
  const [referencia, setReferencia] = useState('');
  const [motivo, setMotivo] = useState('');

  const montoNum = Number(monto);
  const valido =
    cuentaId != null &&
    Number.isFinite(montoNum) &&
    montoNum > 0 &&
    motivo.trim().length > 0;

  function limpiar() {
    setProveedorId(null);
    setCuentaId(null);
    setMonto('');
    setReferencia('');
    setMotivo('');
  }

  function cambiarAbierto(next: boolean) {
    if (!next) limpiar();
    onOpenChange(next);
  }

  function confirmar() {
    if (!valido || cuentaId == null) return;
    const command = {
      cuentaBancariaId: cuentaId,
      monto: montoNum,
      fechaValor,
      motivo: motivo.trim(),
      proveedorId: proveedorId ?? undefined,
      referenciaBancaria: referencia.trim() || undefined,
    };
    registrar.mutate(
      {
        command,
        // Key ligada al contenido (ver useBodyScopedIdempotencyKey):
        // reintento tras timeout no duplica el egreso.
        idempotencyKey: keyFor(command),
      },
      {
        onSuccess: () => {
          toast.success('Pago a cuenta registrado', {
            description:
              'Quedó abierto (NoAplicado); lígalo al pasivo cuando CxP provisione.',
          });
          cambiarAbierto(false);
        },
        onError: (error) => {
          toast.error(
            esApiError(error)
              ? error.problem.title
              : 'No se pudo registrar el pago a cuenta',
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
    <Sheet open={open} onOpenChange={cambiarAbierto}>
      <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Nuevo pago a cuenta</SheetTitle>
          <SheetDescription>
            Egreso ejecutado en banca sin documento ligado. El limbo se
            registra, no se oculta — máximo uno abierto por proveedor (RN-2).
          </SheetDescription>
        </SheetHeader>

        <div className="mt-4 space-y-4">
          <div className="space-y-1">
            <label className="text-xs text-muted-foreground">
              Proveedor (si se conoce)
            </label>
            <ProveedorSelector value={proveedorId} onChange={setProveedorId} />
          </div>

          <div className="space-y-1">
            <label className="text-xs text-muted-foreground">Cuenta de egreso</label>
            <CuentaBancariaSelector value={cuentaId} onChange={setCuentaId} />
          </div>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div className="space-y-1">
              <label className="text-xs text-muted-foreground" htmlFor="pac-monto">
                Monto
              </label>
              <Input
                id="pac-monto"
                type="number"
                inputMode="decimal"
                min={0.01}
                step="0.01"
                value={monto}
                onChange={(e) => setMonto(e.target.value)}
                className="text-right font-mono"
              />
            </div>
            <div className="space-y-1">
              <label className="text-xs text-muted-foreground" htmlFor="pac-fecha">
                Fecha valor
              </label>
              <Input
                id="pac-fecha"
                type="date"
                value={fechaValor}
                onChange={(e) => setFechaValor(e.target.value)}
              />
            </div>
          </div>

          <div className="space-y-1">
            <label className="text-xs text-muted-foreground" htmlFor="pac-ref">
              Referencia bancaria
            </label>
            <Input
              id="pac-ref"
              value={referencia}
              onChange={(e) => setReferencia(e.target.value)}
              maxLength={120}
              placeholder="SPEI-000123"
            />
          </div>

          <div className="space-y-1">
            <label className="text-xs text-muted-foreground" htmlFor="pac-motivo">
              Motivo (obligatorio)
            </label>
            <Textarea
              id="pac-motivo"
              value={motivo}
              onChange={(e) => setMotivo(e.target.value)}
              maxLength={400}
              rows={3}
              placeholder="Pago urgente de refacción — factura en trámite…"
            />
          </div>

          <div className="flex justify-end gap-2">
            <Button
              variant="ghost"
              onClick={() => cambiarAbierto(false)}
              disabled={registrar.isPending}
            >
              Cancelar
            </Button>
            <Button onClick={confirmar} disabled={registrar.isPending || !valido}>
              {registrar.isPending ? 'Registrando…' : 'Registrar pago a cuenta'}
            </Button>
          </div>
        </div>
      </SheetContent>
    </Sheet>
  );
}
