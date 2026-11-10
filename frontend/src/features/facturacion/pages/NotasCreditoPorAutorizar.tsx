import { ErrorState, TableSkeleton } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useState } from 'react';
import type { NotaCreditoPorAutorizar } from '../api/useNotasCreditoAw';
import {
  useAutorizarNotaCredito,
  useFirmarCreditoNotaCredito,
  useNotasCreditoPorAutorizar,
} from '../api/useNotasCreditoAw';
import { formatoImporte } from '../lib/repp-pendiente';

/** Relación CFDI: bonificación = 01, devolución = 03 (card F6.4). */
const claveMotivo = (motivo: string) =>
  motivo === 'Devolucion' ? '03 · Devolución' : '01 · Bonificación';

/** 86e3wjt5r: NC de clientes que llegan de A+W; autorizarlas las timbra (propuesta hasta F6.4). */
export function NotasCreditoPorAutorizar() {
  const query = useNotasCreditoPorAutorizar();
  const autorizar = useAutorizarNotaCredito();
  const firmarCredito = useFirmarCreditoNotaCredito();
  const puedeAutorizar = useHasPermission(PermisosCanonicos.FacturacionNotasCreditoAutorizar);
  // M-087: arriba del umbral firma también Crédito y Cobranza.
  const puedeFirmarCredito = useHasPermission(PermisosCanonicos.FacturacionNotasCreditoAutorizarCredito);
  const [confirmando, setConfirmando] = useState<NotaCreditoPorAutorizar | null>(null);
  const [comoCredito, setComoCredito] = useState(false);
  const [idempotencyKey, setIdempotencyKey] = useState('');
  const [resultado, setResultado] = useState<{ ok: boolean; texto: string } | null>(null);

  function pedirConfirmacion(nc: NotaCreditoPorAutorizar, credito = false) {
    setIdempotencyKey(crypto.randomUUID());
    setComoCredito(credito);
    setConfirmando(nc);
  }
  // Con esta firma, ¿se timbra? Solo si no requiere dos firmas o la otra ya está.
  const timbraAlFirmar = (nc: NotaCreditoPorAutorizar, credito: boolean) =>
    !nc.requiereFirmaCredito || (credito ? !!nc.firmadaGerente : !!nc.firmadaCredito);
  async function confirmar() {
    if (!confirmando) return;
    const nc = confirmando;
    try {
      const r = comoCredito
        ? await firmarCredito.mutateAsync({ id: nc.id, idempotencyKey })
        : await autorizar.mutateAsync({ id: nc.id, idempotencyKey });
      setResultado(
        r.pendiente
          ? {
              ok: true,
              texto: `Firma registrada en la nota ${nc.folioAw ?? r.folio}. Falta la firma de ${
                r.pendiente === 'Gerente' ? 'el gerente' : 'Crédito y Cobranza'
              } para timbrarla.`,
            }
          : r.uuid
          ? {
              ok: true,
              texto: `Nota ${r.folio} timbrada. UUID ${r.uuid}. El saldo de la factura bajó ${formatoImporte(r.total)}.`,
            }
          : {
              ok: false,
              texto: `La nota ${r.folio} quedó en estado ${r.estado}; no se obtuvo UUID. Revisa el estado antes de reintentar.`,
            },
      );
    } catch (error) {
      setResultado({
        ok: false,
        texto: `No se autorizó la nota ${nc.folio}: ${
          esApiError(error) ? (error.problem.detail ?? error.problem.title) : 'error inesperado'
        }`,
      });
    } finally {
      setConfirmando(null);
    }
  }

  const items = query.data ?? [];
  return (
    <main className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        titulo={<>Notas de crédito por autorizar</>}
        descripcion={
          <>
            Notas de crédito de clientes que llegaron de A+W. Al autorizarlas se timbran y bajan el
            saldo de la factura.
          </>
        }
      />
      {resultado && (
        <div
          role={resultado.ok ? 'status' : 'alert'}
          className={`rounded-lg bg-surface-card p-3 shadow-card-flat ${resultado.ok ? '' : 'text-danger-fg'}`}
        >
          {resultado.texto}
        </div>
      )}
      <div className="min-h-0 flex-1 overflow-auto">
        {query.isError ? (
          <ErrorState onRetry={() => void query.refetch()} />
        ) : query.isLoading ? (
          <TableSkeleton />
        ) : !items.length ? (
          <p className="p-4 text-ink-muted">No hay notas de crédito por autorizar.</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Folio A+W</TableHead>
                <TableHead>Cliente</TableHead>
                <TableHead>Factura relacionada</TableHead>
                <TableHead>Vendedor A+W</TableHead>
                <TableHead>Motivo</TableHead>
                <TableHead className="text-right tabular-nums">Importe</TableHead>
                <TableHead>Fecha</TableHead>
                <TableHead>Firmas</TableHead>
                {(puedeAutorizar || puedeFirmarCredito) && <TableHead>Acción</TableHead>}
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((nc) => (
                <TableRow key={nc.id} className="h-12">
                  <TableCell className="font-mono text-xs">{nc.folioAw ?? nc.folio}</TableCell>
                  <TableCell>
                    <p className="font-medium">{nc.receptorNombre}</p>
                    <p className="font-mono text-xs text-ink-muted">{nc.receptorRfc}</p>
                  </TableCell>
                  <TableCell className="font-mono text-xs">
                    {nc.facturaFolio ?? 'Sin factura'}
                  </TableCell>
                  <TableCell>{nc.vendedorAw ?? '—'}</TableCell>
                  <TableCell>{claveMotivo(nc.motivo)}</TableCell>
                  <TableCell className="text-right tabular-nums">
                    {formatoImporte(nc.total)} {nc.moneda}
                  </TableCell>
                  <TableCell>{nc.fechaRecibida?.slice(0, 10) ?? '—'}</TableCell>
                  <TableCell className="text-xs">
                    {nc.requiereFirmaCredito ? (
                      <>
                        <p>Gerente: {nc.firmadaGerente ? 'firmó' : 'pendiente'}</p>
                        <p>Crédito y Cobranza: {nc.firmadaCredito ? 'firmó' : 'pendiente'}</p>
                      </>
                    ) : (
                      'Gerente'
                    )}
                  </TableCell>
                  {(puedeAutorizar || puedeFirmarCredito) && (
                    <TableCell className="space-x-2 whitespace-nowrap">
                      {puedeAutorizar && !nc.firmadaGerente && (
                        <Button
                          size="sm"
                          disabled={autorizar.isPending || firmarCredito.isPending}
                          onClick={() => pedirConfirmacion(nc)}
                        >
                          Autorizar
                        </Button>
                      )}
                      {puedeFirmarCredito && nc.requiereFirmaCredito && !nc.firmadaCredito && (
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={autorizar.isPending || firmarCredito.isPending}
                          onClick={() => pedirConfirmacion(nc, true)}
                        >
                          Firmar por Crédito y Cobranza
                        </Button>
                      )}
                    </TableCell>
                  )}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </div>
      <AlertDialog
        open={confirmando !== null}
        onOpenChange={(abierto) => !abierto && setConfirmando(null)}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {confirmando && !timbraAlFirmar(confirmando, comoCredito)
                ? '¿Firmar la nota de crédito?'
                : '¿Autorizar y timbrar la nota de crédito?'}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {confirmando &&
                `${confirmando.folioAw ?? confirmando.folio} · ${confirmando.receptorNombre} · ${formatoImporte(confirmando.total)} sobre la factura ${confirmando.facturaFolio ?? 'sin folio'}. ${
                  timbraAlFirmar(confirmando, comoCredito)
                    ? 'Se timbrará ante el SAT y no se puede deshacer.'
                    : `Por su importe requiere también la firma de ${comoCredito ? 'el gerente' : 'Crédito y Cobranza'}; se timbrará cuando estén las dos.`
                }`}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancelar</AlertDialogCancel>
            <AlertDialogAction onClick={() => void confirmar()}>
              {confirmando && !timbraAlFirmar(confirmando, comoCredito) ? 'Firmar' : 'Autorizar y timbrar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </main>
  );
}
