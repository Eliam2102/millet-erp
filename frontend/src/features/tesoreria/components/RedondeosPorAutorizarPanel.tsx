import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { errorUsuario } from '@/lib/error-usuario';
import { useBodyScopedIdempotencyKey } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  useAutorizarRedondeoPago,
  useRechazarRedondeoPago,
  useRedondeosPorAutorizar,
} from '@/features/tesoreria/api/useRedondeoPago';
import { formatoMonto } from '@/features/tesoreria/lib/formato';

/**
 * M-326 (b): solicitudes de redondeo (≤$0.99) de pagos a proveedor que esperan al Gerente de Área. Solo se muestra con el permiso
 * «autorizar redondeo» y cuando hay solicitudes; quien solicitó no puede resolver la suya.
 */
export function RedondeosPorAutorizarPanel() {
  const puede = useHasPermission(PermisosCanonicos.TesoreriaPagosAutorizarRedondeo);
  const usuarioId = useAuthStore((s) => s.user?.id ?? null);
  const query = useRedondeosPorAutorizar(puede);
  const autorizar = useAutorizarRedondeoPago();
  const rechazar = useRechazarRedondeoPago();
  const keyFor = useBodyScopedIdempotencyKey();
  const [motivos, setMotivos] = useState<Record<string, string>>({});

  const items = query.data ?? [];
  if (!puede || items.length === 0) return null;

  return (
    <section aria-label="Redondeos por autorizar" className="space-y-2 rounded-md border bg-surface-card p-3">
      <h2 className="text-sm font-medium">Redondeos por autorizar ({items.length})</h2>
      <ul className="divide-y">
        {items.map((s) => {
          const propia = usuarioId != null && s.solicitadoPor === usuarioId;
          const motivo = (motivos[s.id] ?? '').trim();
          const ocupado = autorizar.isPending || rechazar.isPending;
          return (
            <li key={s.id} className="flex flex-wrap items-center gap-2 py-2">
              <span className="font-mono text-xs text-ink-muted">Factura {s.facturaProveedorId.slice(0, 8)}</span>
              <span className="font-mono tabular-nums">
                {s.redondeo > 0 ? 'A favor' : 'En contra'} {formatoMonto(Math.abs(s.redondeo), 'MXN')}
              </span>
              <span className="min-w-40 flex-1 text-xs text-ink-muted">{s.motivo}</span>
              {propia ? (
                <span className="text-xs text-ink-muted">La resuelve otra persona (tú la solicitaste).</span>
              ) : (
                <>
                  <Input
                    className="w-48"
                    aria-label="Motivo del rechazo"
                    placeholder="Motivo si rechazas"
                    maxLength={400}
                    value={motivos[s.id] ?? ''}
                    onChange={(e) => setMotivos((prev) => ({ ...prev, [s.id]: e.target.value }))}
                  />
                  <Button
                    size="sm"
                    disabled={ocupado}
                    onClick={() =>
                      autorizar.mutate(
                        { id: s.id, idempotencyKey: keyFor({ accion: 'autorizar', id: s.id }) },
                        {
                          onSuccess: () => toast.success('Redondeo autorizado'),
                          onError: (e) => toast.error(errorUsuario(e, 'No se pudo autorizar el redondeo')),
                        },
                      )
                    }
                  >
                    Autorizar
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={ocupado || motivo === ''}
                    title={motivo === '' ? 'Captura el motivo del rechazo' : undefined}
                    onClick={() =>
                      rechazar.mutate(
                        { id: s.id, motivo, idempotencyKey: keyFor({ accion: 'rechazar', id: s.id, motivo }) },
                        {
                          onSuccess: () => toast.success('Redondeo rechazado'),
                          onError: (e) => toast.error(errorUsuario(e, 'No se pudo rechazar el redondeo')),
                        },
                      )
                    }
                  >
                    Rechazar
                  </Button>
                </>
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
