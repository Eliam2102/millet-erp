import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { DialogBody } from '@/components/ui/dialog';
import { useState } from 'react';
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
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { AvisoEnLinea } from '@/components/erp';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { hoyLocalISO } from '@/lib/datetime';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { usePagarDevolucion } from '@/features/tesoreria/api/useTesoreria';
import {
  OrigenDevolucionCliente,
  type DevolucionClienteResponse,
} from '@/features/tesoreria/api/types';
import { CuentaBancariaSelector } from './CuentaBancariaSelector';

export interface PagarDevolucionDialogProps {
  devolucion: DevolucionClienteResponse;
  abierto: boolean;
  onCerrar: () => void;
}

/**
 * Registra el pago de una devolución autorizada (T9.6, CA9.11). El ERP no está
 * conectado al banco: el operador transfiere en la banca y aquí confirma que
 * verificó fondos. Depósito erróneo: sale de la misma cuenta donde entró
 * (contramovimiento ligado); nota de crédito: se elige la cuenta de egreso.
 */
export function PagarDevolucionDialog({
  devolucion: d,
  abierto,
  onCerrar,
}: PagarDevolucionDialogProps) {
  const pagar = usePagarDevolucion();
  const keyFor = useBodyScopedIdempotencyKey();
  const [fechaValor, setFechaValor] = useState(() => hoyLocalISO());
  const [referencia, setReferencia] = useState('');
  const [cuentaId, setCuentaId] = useState<string | null>(null);
  const [fondosVerificados, setFondosVerificados] = useState(false);
  const [aviso, setAviso] = useState<string | null>(null);

  const esNc = d.origen === OrigenDevolucionCliente.NotaCredito;
  const bloqueo =
    esNc && cuentaId == null
      ? 'Selecciona la cuenta de egreso'
      : !fondosVerificados
        ? 'Confirma que verificaste fondos en la banca'
        : null;

  function cerrar() {
    setCuentaId(null);
    setReferencia('');
    setFondosVerificados(false);
    setAviso(null);
    setFechaValor(hoyLocalISO());
    onCerrar();
  }

  function confirmar() {
    if (bloqueo != null) return;
    setAviso(null);
    const body = {
      id: d.id,
      versionEsperada: d.version,
      fechaValor,
      cuentaBancariaId: esNc ? (cuentaId ?? undefined) : undefined,
      referenciaBancaria: referencia.trim() || undefined,
      fondosVerificados: true,
    };
    // Key ligada al contenido: un reintento tras timeout no duplica el desembolso.
    pagar.mutate(
      { ...body, idempotencyKey: keyFor(body) },
      {
        onSuccess: () => {
          toast.success('Devolución pagada', {
            description:
              'Se registró el egreso ligado al origen; el movimiento original se conserva.',
          });
          cerrar();
        },
        onError: (error) => {
          if (esApiError(error)) {
            setAviso(error.problem.detail ?? error.problem.title);
            toast.error(error.problem.title, { description: error.problem.detail ?? undefined });
          } else toast.error('No se pudo registrar el pago de la devolución.');
        },
      },
    );
  }

  return (
    <Dialog open={abierto} onOpenChange={(v) => !v && !pagar.isPending && cerrar()}>
      <DialogContent size="lg">
        <DialogHeader>
          <DialogTitle>Registrar pago de la devolución</DialogTitle>
          <DialogDescription>
            La transferencia se ejecuta en la banca; aquí se registra el hecho bancario. No se puede
            deshacer.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          <dl className="grid grid-cols-2 gap-3 rounded-md border border-line-row p-3 text-sm">
            <div>
              <dt className="text-xs text-ink-muted">Importe</dt>
              <dd className="tabular-nums">
                <MoneyDisplay amount={d.importe} currency={d.moneda} strong />
              </dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Cliente</dt>
              <dd>{d.clienteRazonSocial ?? '—'}</dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Banco destino</dt>
              <dd>{d.destinoBanco ?? '—'}</dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Titular</dt>
              <dd>{d.destinoTitular ?? '—'}</dd>
            </div>
            <div className="col-span-2">
              <dt className="text-xs text-ink-muted">CLABE destino</dt>
              <dd className="font-mono">{d.destinoClabe ?? '—'}</dd>
            </div>
          </dl>
          {aviso && (
            <AvisoEnLinea tono="error" titulo="No se pudo registrar el pago">
              {aviso}
            </AvisoEnLinea>
          )}
          <div className="space-y-3">
            {esNc ? (
              <div className="space-y-1">
                <label className="text-xs text-ink-muted">Cuenta de egreso ({d.moneda})</label>
                <CuentaBancariaSelector value={cuentaId} onChange={setCuentaId} moneda={d.moneda} />
              </div>
            ) : (
              <p className="text-xs text-ink-muted">
                Sale de la misma cuenta donde entró el depósito (contramovimiento ligado).
              </p>
            )}
            <FormSeccion numero={1} titulo="Datos de la operación">
              <FormCampo span={6}>
                <div className="space-y-1">
                  <label className="text-xs text-ink-muted" htmlFor="pagar-devolucion-fecha">
                    Fecha valor *
                  </label>
                  <Input
                    id="pagar-devolucion-fecha"
                    type="date"
                    value={fechaValor}
                    onChange={(e) => setFechaValor(e.target.value)}
                  />
                </div>
              </FormCampo>
              <FormCampo span={6}>
                <div className="space-y-1">
                  <label className="text-xs text-ink-muted" htmlFor="pagar-devolucion-referencia">
                    Referencia bancaria
                  </label>
                  <Input
                    id="pagar-devolucion-referencia"
                    value={referencia}
                    onChange={(e) => setReferencia(e.target.value)}
                    maxLength={120}
                    placeholder="SPEI-000123"
                  />
                </div>
              </FormCampo>
            </FormSeccion>
            <div className="flex items-start gap-2">
              <Checkbox
                id="pagar-devolucion-fondos"
                checked={fondosVerificados}
                onCheckedChange={(v) => setFondosVerificados(v === true)}
              />
              <div>
                <label htmlFor="pagar-devolucion-fondos" className="text-sm">
                  Verifiqué en la banca que la cuenta tiene fondos para esta devolución *
                </label>
                <p className="block text-xs text-ink-muted">
                  El ERP no está conectado al banco: el saldo en libros puede no estar al día.
                </p>
              </div>
            </div>
          </div>
          {bloqueo && !pagar.isPending && (
            <p className="text-right text-xs text-ink-muted">{bloqueo}</p>
          )}
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={cerrar} disabled={pagar.isPending}>
            Cancelar
          </Button>
          <Button onClick={confirmar} disabled={pagar.isPending || bloqueo != null}>
            {pagar.isPending ? 'Registrando…' : 'Registrar pago'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
