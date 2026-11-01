import { useState } from 'react';
import { toast } from 'sonner';
import { Link } from '@tanstack/react-router';
import { Ban } from 'lucide-react';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Button } from '@/components/ui/button';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { useCancelarFolioTransferencia } from '@/features/tesoreria/api/useTesoreria';
import {
  EstadoFolioTransferencia,
  type FolioTransferenciaResponse,
} from '@/features/tesoreria/api/types';
import { EstadoFolioTransferenciaBadge } from './Badges';
import { MotivoDialog } from './MotivoDialog';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';

/**
 * F6.6: folios de confirmación de transferencia (folio, cliente, importe,
 * pedido, estado). Ingresos cancela uno vigente con motivo; uno usado ya lo
 * aplicó Caja y solo vuelve a vigente si se cancela el cobro.
 */
export function FoliosTransferenciaTabla({
  folios,
  mostrarMovimiento = false,
}: {
  folios: readonly FolioTransferenciaResponse[];
  mostrarMovimiento?: boolean;
}) {
  const puedeCancelar = useHasPermission(PermisosCanonicos.TesoreriaFoliosTransferenciaCancelar);
  const cancelar = useCancelarFolioTransferencia();
  const keyFor = useBodyScopedIdempotencyKey();
  const [cancelando, setCancelando] = useState<FolioTransferenciaResponse | null>(null);

  function confirmarCancelacion(motivo: string) {
    if (cancelando == null) return;
    const body = { id: cancelando.id, motivo };
    cancelar.mutate(
      { ...body, idempotencyKey: keyFor(body) },
      {
        onSuccess: (f) => {
          toast.success(`Folio ${f.folio} cancelado`);
          setCancelando(null);
        },
        onError: (e) =>
          toast.error(esApiError(e) ? (e.problem.detail ?? e.problem.title) : 'No se pudo cancelar el folio'),
      },
    );
  }

  return (
    <>
      <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
        <TableContainer>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Folio</TableHead>
                <TableHead>Cliente</TableHead>
                <TableHead>Pedido</TableHead>
                {mostrarMovimiento && <TableHead>Transferencia</TableHead>}
                <TableHead className="text-right">Importe</TableHead>
                <TableHead>Estado</TableHead>
                <TableHead>Emitido</TableHead>
                {puedeCancelar && <TableHead className="w-24" />}
              </TableRow>
            </TableHeader>
            <TableBody>
              {folios.map((f) => (
                <TableRow key={f.id}>
                  <TableCell className="font-mono text-xs">{f.folio}</TableCell>
                  <TableCell className="max-w-64">
                    <span className="block truncate">{f.clienteRazonSocial ?? '—'}</span>
                    <span className="font-mono text-xs text-ink-muted">{f.clienteClave ?? ''}</span>
                  </TableCell>
                  <TableCell className="text-xs">{f.pedido ?? '—'}</TableCell>
                  {mostrarMovimiento && (
                    <TableCell className="text-xs">
                      <Link
                        to="/tesoreria/movimientos/$id"
                        params={{ id: f.movimientoId }}
                        className="font-mono text-brand hover:underline"
                      >
                        {f.referenciaBancaria ?? 'Ver ingreso'}
                      </Link>
                    </TableCell>
                  )}
                  <TableCell className="text-right">
                    <MoneyDisplay amount={f.importe} currency="MXN" strong />
                  </TableCell>
                  <TableCell>
                    <EstadoFolioTransferenciaBadge estado={f.estado} />
                    {f.estado === EstadoFolioTransferencia.Cancelado && f.motivoCancelacion && (
                      <span className="block max-w-48 truncate text-xs text-ink-muted" title={f.motivoCancelacion}>
                        {f.motivoCancelacion}
                      </span>
                    )}
                  </TableCell>
                  <TableCell>
                    <DateTimeDisplay value={f.emitidoEn} variant="corta" />
                  </TableCell>
                  {puedeCancelar && (
                    <TableCell>
                      {f.estado === EstadoFolioTransferencia.Vigente && (
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() => setCancelando(f)}
                          aria-label={`Cancelar folio ${f.folio}`}
                        >
                          <Ban className="mr-1 h-3 w-3" aria-hidden="true" />
                          Cancelar
                        </Button>
                      )}
                    </TableCell>
                  )}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </div>
      <MotivoDialog
        open={cancelando != null}
        onOpenChange={(open) => (open ? undefined : setCancelando(null))}
        titulo={`Cancelar folio ${cancelando?.folio ?? ''}`}
        descripcion="Caja ya no podrá aplicar este folio. Su importe vuelve a quedar disponible en el ingreso."
        confirmLabel="Cancelar folio"
        onConfirm={confirmarCancelacion}
        pending={cancelar.isPending}
      />
    </>
  );
}
