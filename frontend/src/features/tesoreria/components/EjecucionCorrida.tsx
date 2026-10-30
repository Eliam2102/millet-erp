import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { useRegistrarEjecucionCorrida } from '@/features/tesoreria/api/useTesoreria';
import type { CorridaResponse } from '@/features/tesoreria/api/types';
import { pagosPendientes } from '@/features/tesoreria/lib/corridas';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';

/**
 * Registro de la ejecución (T9.1-B): el dinero se mueve en la banca; aquí se captura, por proveedor, la referencia y la
 * fecha del pago. Cada proveedor es un pago. Ejecutar parte de la corrida deja el resto pendiente.
 */
export function EjecucionCorrida({ corrida, onError }: { corrida: CorridaResponse; onError: (mensaje: string) => void }) {
  const registrar = useRegistrarEjecucionCorrida();
  const keyFor = useBodyScopedIdempotencyKey();
  const hoy = hoyLocalISO();
  const [captura, setCaptura] = useState<Record<string, { referencia: string; fecha: string }>>({});
  const nombres = new Map(corrida.proveedores.map((p) => [p.proveedorId, p.proveedorRazonSocial ?? 'Proveedor sin nombre']));
  const pendientes = pagosPendientes(corrida.lineas);

  if (pendientes.length === 0) {
    return <p className="text-xs text-ink-muted">No quedan pagos pendientes en esta corrida.</p>;
  }

  function registrarPago(proveedorId: string, facturas: string[]) {
    const c = captura[proveedorId];
    const body = {
      id: corrida.id,
      versionEsperada: corrida.version,
      pagos: [{ facturas, referenciaBancaria: (c?.referencia ?? '').trim(), fechaValor: c?.fecha || hoy }],
    };
    onError('');
    registrar.mutate(
      { ...body, idempotencyKey: keyFor(body) },
      {
        onSuccess: () => toast.success('Pago registrado'),
        onError: (e) =>
          esApiError(e) ? onError(e.problem.detail ?? e.problem.title) : toast.error('No se pudo registrar el pago.'),
      },
    );
  }

  return (
    <ul className="space-y-2">
      {pendientes.map((p) => {
        const c = captura[p.proveedorId] ?? { referencia: '', fecha: hoy };
        const idRef = `ref-${p.proveedorId}`;
        const idFecha = `fecha-${p.proveedorId}`;
        return (
          <li key={p.proveedorId} className="grid items-end gap-3 rounded-lg bg-surface-card p-3 shadow-card-flat md:grid-cols-[1fr_200px_160px_auto]">
            <div>
              <p className="font-medium">{nombres.get(p.proveedorId)}</p>
              <p className="text-xs text-ink-muted">
                {p.facturas.length} {p.facturas.length === 1 ? 'factura' : 'facturas'} · <MoneyDisplay amount={p.neto} currency={corrida.moneda} strong />
              </p>
            </div>
            <div className="space-y-1">
              <label htmlFor={idRef} className="text-xs text-ink-muted">Referencia bancaria</label>
              <Input id={idRef} value={c.referencia} maxLength={120} onChange={(e) => setCaptura({ ...captura, [p.proveedorId]: { ...c, referencia: e.target.value } })} />
            </div>
            <div className="space-y-1">
              <label htmlFor={idFecha} className="text-xs text-ink-muted">Fecha del pago</label>
              <Input id={idFecha} type="date" value={c.fecha} onChange={(e) => setCaptura({ ...captura, [p.proveedorId]: { ...c, fecha: e.target.value } })} />
            </div>
            <Button
              size="sm"
              disabled={registrar.isPending || c.referencia.trim() === ''}
              title={c.referencia.trim() === '' ? 'Captura la referencia bancaria.' : undefined}
              onClick={() => registrarPago(p.proveedorId, p.facturas)}
            >
              Registrar pago
            </Button>
          </li>
        );
      })}
    </ul>
  );
}
