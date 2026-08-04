import { hoyLocalISO } from '@/lib/datetime';
import { useMemo, useState } from 'react';
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
import { CuentaBancariaSelector } from './CuentaBancariaSelector';
import { useRegistrarPago } from '@/features/tesoreria/api/useTesoreria';
import type { PasivoPendienteResponse } from '@/features/tesoreria/api/types';
import { formatoFecha, formatoMonto } from '@/features/tesoreria/lib/formato';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';

export interface RegistrarPagoSheetProps {
  /** Pasivos seleccionados en la bandeja (mismo proveedor y moneda — validado aquí y en backend). */
  pasivos: PasivoPendienteResponse[];
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: () => void;
}

/**
 * Sheet "Registrar pago" (TES-FE-PR2, 05-frontend-diseno §4.2): resumen
 * de pasivos con importes editables (default: saldo), cuenta de egreso
 * filtrada por moneda (RN-3 en UI), fecha valor y referencia bancaria.
 * El operador ejecuta la transferencia en la banca y AQUÍ registra el
 * hecho — al confirmar, CxP recibe <c>aplicado.v1</c> por factura.
 */
export function RegistrarPagoSheet({
  pasivos,
  open,
  onOpenChange,
  onSuccess,
}: RegistrarPagoSheetProps) {
  const registrar = useRegistrarPago();
  const keyFor = useBodyScopedIdempotencyKey();

  const [cuentaId, setCuentaId] = useState<string | null>(null);
  const [fechaValor, setFechaValor] = useState(() =>
    hoyLocalISO(),
  );
  const [referencia, setReferencia] = useState('');
  // Importes: default = saldo del pasivo; el usuario puede sobrescribir.
  // Sin useEffect (lint set-state-in-effect): estado solo de overrides y
  // el efectivo se deriva en render. Se limpian al cerrar el Sheet.
  const [overrides, setOverrides] = useState<Record<string, string>>({});
  const importes = useMemo(
    () =>
      Object.fromEntries(
        pasivos.map((p) => [
          p.facturaProveedorId,
          overrides[p.facturaProveedorId] ?? String(p.saldoPendiente),
        ]),
      ),
    [pasivos, overrides],
  );

  const moneda = pasivos[0]?.moneda ?? null;
  const proveedor = pasivos[0]?.proveedorRazonSocial ?? pasivos[0]?.proveedorId;

  const total = useMemo(
    () =>
      pasivos.reduce(
        (acc, p) => acc + (Number(importes[p.facturaProveedorId]) || 0),
        0,
      ),
    [pasivos, importes],
  );

  const importesValidos = pasivos.every((p) => {
    const v = Number(importes[p.facturaProveedorId]);
    return Number.isFinite(v) && v > 0 && v <= p.saldoPendiente;
  });

  function confirmar() {
    if (cuentaId == null || !importesValidos) return;
    const command = {
      cuentaBancariaId: cuentaId,
      fechaValor,
      referenciaBancaria: referencia.trim() || undefined,
      aplicaciones: pasivos.map((p) => ({
        facturaProveedorId: p.facturaProveedorId,
        importe: Number(importes[p.facturaProveedorId]),
      })),
    };
    registrar.mutate(
      {
        command,
        // Key ligada al contenido: un reintento tras timeout re-envía la
        // MISMA key+body y el backend devuelve la respuesta cacheada, en vez
        // de registrar un SEGUNDO desembolso. Corregir el pago tras un
        // rechazo cambia el body → key nueva (sin KEY_REUSED).
        idempotencyKey: keyFor(command),
      },
      {
        onSuccess: (r) => {
          toast.success('Pago registrado', {
            description: `${formatoMonto(r.monto, r.moneda)} aplicado a ${r.aplicaciones.length} pasivo(s). CxP recibirá el evento.`,
          });
          cambiarAbierto(false);
          onSuccess();
        },
        onError: (error) => {
          toast.error(
            esApiError(error) ? error.problem.title : 'No se pudo registrar el pago',
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

  function cambiarAbierto(next: boolean) {
    if (!next) {
      // Reset completo al cerrar: el Sheet queda montado en BandejaPagos, así
      // que sin esto la referencia bancaria y la cuenta (¡de otra moneda!)
      // del pago anterior persistían al reabrir para otro proveedor →
      // referencia SPEI equivocada / cuenta de moneda inválida enviable.
      setOverrides({});
      setCuentaId(null);
      setReferencia('');
      setFechaValor(hoyLocalISO());
    }
    onOpenChange(next);
  }

  return (
    <Sheet open={open} onOpenChange={cambiarAbierto}>
      <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-2xl">
        <SheetHeader>
          <SheetTitle>Registrar pago a proveedor</SheetTitle>
          <SheetDescription>
            {pasivos.length} pasivo(s) de {proveedor}. La transferencia se
            ejecuta en la banca; aquí se registra el hecho bancario.
          </SheetDescription>
        </SheetHeader>

        <div className="mt-4 space-y-4">
          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-sm">
              <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 font-medium">Factura</th>
                  <th className="px-3 py-2 font-medium">Vence</th>
                  <th className="px-3 py-2 text-right font-medium">Saldo</th>
                  <th className="px-3 py-2 text-right font-medium">Importe a pagar</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {pasivos.map((p) => {
                  const v = Number(importes[p.facturaProveedorId]);
                  const invalido = !Number.isFinite(v) || v <= 0 || v > p.saldoPendiente;
                  return (
                    <tr key={p.facturaProveedorId}>
                      <td className="px-3 py-2">
                        <p className="font-mono text-xs">
                          {p.folioProveedor ?? p.facturaProveedorId.slice(0, 8)}
                        </p>
                      </td>
                      <td className="px-3 py-2 text-xs">
                        {formatoFecha(p.fechaVencimiento)}
                      </td>
                      <td className="px-3 py-2 text-right font-mono tabular-nums">
                        {formatoMonto(p.saldoPendiente, p.moneda)}
                      </td>
                      <td className="px-3 py-2 text-right">
                        <Input
                          type="number"
                          inputMode="decimal"
                          min={0.01}
                          max={p.saldoPendiente}
                          step="0.01"
                          className={`ml-auto w-36 text-right font-mono ${invalido ? 'border-destructive' : ''}`}
                          aria-label={`Importe para ${p.folioProveedor ?? p.facturaProveedorId}`}
                          value={importes[p.facturaProveedorId] ?? ''}
                          onChange={(e) =>
                            setOverrides((prev) => ({
                              ...prev,
                              [p.facturaProveedorId]: e.target.value,
                            }))
                          }
                        />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          {pasivos[0]?.banco != null || pasivos[0]?.clabe != null ? (
            <p className="text-xs text-muted-foreground">
              Datos bancarios del proveedor: {pasivos[0]?.banco ?? '—'} ·{' '}
              <span className="font-mono">{pasivos[0]?.clabe ?? 'sin CLABE'}</span>
              {pasivos[0]?.beneficiario ? ` · ${pasivos[0].beneficiario}` : ''}
            </p>
          ) : (
            <p className="text-xs text-amber-700">
              El proveedor no tiene datos bancarios capturados en Datos
              Maestros — captúralos para transferir con confianza.
            </p>
          )}

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div className="space-y-1 sm:col-span-2">
              <label className="text-xs text-muted-foreground">
                Cuenta de egreso {moneda ? `(${moneda})` : ''}
              </label>
              <CuentaBancariaSelector
                value={cuentaId}
                onChange={setCuentaId}
                moneda={moneda}
              />
            </div>
            <div className="space-y-1">
              <label className="text-xs text-muted-foreground" htmlFor="fecha-valor">
                Fecha valor
              </label>
              <Input
                id="fecha-valor"
                type="date"
                value={fechaValor}
                onChange={(e) => setFechaValor(e.target.value)}
              />
            </div>
            <div className="space-y-1">
              <label className="text-xs text-muted-foreground" htmlFor="referencia">
                Referencia bancaria
              </label>
              <Input
                id="referencia"
                value={referencia}
                onChange={(e) => setReferencia(e.target.value)}
                maxLength={120}
                placeholder="SPEI-000123"
              />
            </div>
          </div>

          <div className="flex items-center justify-between rounded-md bg-muted/50 px-4 py-3">
            <span className="text-sm text-muted-foreground">Total a pagar</span>
            <span className="font-mono text-lg font-semibold tabular-nums">
              {moneda ? formatoMonto(total, moneda) : total.toFixed(2)}
            </span>
          </div>

          <div className="flex justify-end gap-2">
            <Button
              variant="ghost"
              onClick={() => cambiarAbierto(false)}
              disabled={registrar.isPending}
            >
              Cancelar
            </Button>
            <Button
              onClick={confirmar}
              disabled={
                registrar.isPending ||
                cuentaId == null ||
                pasivos.length === 0 ||
                !importesValidos
              }
            >
              {registrar.isPending ? 'Registrando…' : 'Registrar pago'}
            </Button>
          </div>
        </div>
      </SheetContent>
    </Sheet>
  );
}
