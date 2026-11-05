import { useState } from 'react';
import { Inbox } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { AvisoEnLinea, ConfirmarAccionDialog, EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { useAuthStore } from '@/lib/auth/auth-store';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { MotivoDialog } from '../components/MotivoDialog';
import {
  useAutorizarPagoComercioExterior,
  usePagosComercioExterior,
  useRechazarPagoComercioExterior,
  type AutorizacionPagoComercioExteriorResponse as Pago,
  type EstadoPagoComercioExterior,
} from '../api/usePagosComercioExterior';

const ESTADO: Record<EstadoPagoComercioExterior, { texto: string; tono: 'warning' | 'info' | 'success' | 'danger' }> = {
  PendienteNivel1: { texto: 'Pendiente de nivel 1', tono: 'warning' },
  PendienteNivel2: { texto: 'Pendiente de nivel 2', tono: 'info' },
  Autorizado: { texto: 'Autorizado', tono: 'success' },
  Rechazado: { texto: 'Rechazado', tono: 'danger' },
};

/**
 * <c>Autorización de pagos de comercio exterior</c> (I13.3): nivel 1 (Compras de MP en importación,
 * Documentación de Exportación en exportación) y nivel 2 (Dirección; basta una firma). Sin las dos,
 * el pasivo no es seleccionable en la corrida. Quien firmó el nivel 1 no firma el nivel 2: el
 * servidor lo impide y aquí se deshabilita el botón.
 */
export function BandejaPagosComercioExterior() {
  const [soloMiNivel, setSoloMiNivel] = useState(true);
  const query = usePagosComercioExterior({ soloMiNivel, limit: 100 });
  const keyFor = useBodyScopedIdempotencyKey();
  const usuarioId = useAuthStore((s) => s.user?.id);
  const autorizar = useAutorizarPagoComercioExterior();
  const rechazar = useRechazarPagoComercioExterior();
  const [aFirmar, setAFirmar] = useState<Pago | null>(null);
  const [aRechazar, setARechazar] = useState<Pago | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);

  const items = query.data?.items ?? [];

  function alError(e: unknown, fallback: string) {
    if (esApiError(e)) setAviso(e.problem.detail ?? e.problem.title);
    else toast.error(fallback);
  }

  function ejecutarFirma(p: Pago) {
    setAviso(null);
    const nivel = (p.nivelPendiente ?? 1) as 1 | 2;
    const body = { facturaProveedorId: p.facturaProveedorId, nivel };
    autorizar.mutate(
      { ...body, idempotencyKey: keyFor(body) },
      {
        onSuccess: () => {
          toast.success(`Nivel ${nivel} autorizado`);
          setAFirmar(null);
        },
        onError: (e) => {
          setAFirmar(null);
          alError(e, 'No se pudo autorizar el pago.');
        },
      },
    );
  }

  function ejecutarRechazo(p: Pago, motivo: string) {
    setAviso(null);
    const body = { facturaProveedorId: p.facturaProveedorId, motivo };
    rechazar.mutate(
      { ...body, idempotencyKey: keyFor(body) },
      {
        onSuccess: () => {
          toast.success('Pago rechazado');
          setARechazar(null);
        },
        onError: (e) => {
          setARechazar(null);
          alError(e, 'No se pudo rechazar el pago.');
        },
      },
    );
  }

  return (
    <div className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        titulo="Autorización de pagos de comercio exterior"
        descripcion="Importación y exportación se autorizan en dos niveles antes de entrar a una corrida de pago: nivel 1 por Compras de MP o Documentación de Exportación; nivel 2 por la Dirección."
        acciones={
          <Button variant="outline" onClick={() => setSoloMiNivel((v) => !v)}>
            {soloMiNivel ? 'Ver todos' : 'Ver solo mi bandeja'}
          </Button>
        }
      />
      {aviso && <AvisoEnLinea tono="error">{aviso}</AvisoEnLinea>}

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los pagos"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={5}
          columns={[{ width: 'w-24' }, { width: 'w-48' }, { width: 'w-24' }, { width: 'w-28' }, { width: 'w-32' }, { width: 'w-32' }]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          icon={<Inbox className="h-10 w-10" />}
          title={soloMiNivel ? 'Nada pendiente de tu firma' : 'Sin pagos de comercio exterior'}
          description="Los pasivos de importación y exportación con saldo aparecen aquí hasta tener sus dos firmas."
        />
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <TableContainer>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Tipo</TableHead>
                  <TableHead>Proveedor</TableHead>
                  <TableHead>Factura</TableHead>
                  <TableHead className="text-right">Saldo</TableHead>
                  <TableHead>Vence</TableHead>
                  <TableHead>Nivel 1</TableHead>
                  <TableHead>Nivel 2</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead className="text-right">Acciones</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((p) => {
                  const e = ESTADO[p.estado];
                  return (
                    <TableRow key={p.facturaProveedorId} className="hover:bg-surface-subtle">
                      <TableCell>{p.tipo === 'Importacion' ? 'Importación' : 'Exportación'}</TableCell>
                      <TableCell>{p.proveedorRazonSocial ?? '—'}</TableCell>
                      <TableCell className="font-mono text-xs">{p.folioProveedor ?? '—'}</TableCell>
                      <TableCell className="text-right">
                        <MoneyDisplay amount={p.saldoPendiente} currency={p.moneda} strong />
                      </TableCell>
                      <TableCell className="text-xs">{p.fechaVencimiento}</TableCell>
                      <TableCell className="text-xs">
                        {p.nivel1UsuarioId ? (
                          <>
                            {p.nivel1Nombre ?? 'Firmado'}
                            <span className="block text-ink-muted"><DateTimeDisplay value={p.nivel1En} variant="corta" /></span>
                          </>
                        ) : '—'}
                      </TableCell>
                      <TableCell className="text-xs">
                        {p.nivel2UsuarioId ? (
                          <>
                            {p.nivel2Nombre ?? 'Firmado'}
                            <span className="block text-ink-muted"><DateTimeDisplay value={p.nivel2En} variant="corta" /></span>
                          </>
                        ) : '—'}
                      </TableCell>
                      <TableCell>
                        <Badge variant={e.tono}>{e.texto}</Badge>
                        {p.motivoRechazo && (
                          <span className="block max-w-48 truncate text-xs text-ink-muted" title={p.motivoRechazo}>
                            {p.motivoRechazo}
                          </span>
                        )}
                      </TableCell>
                      <TableCell className="text-right">
                        {(p.puedoFirmar || p.puedoRechazar) && (
                          <div className="flex justify-end gap-2">
                            {p.puedoFirmar && (
                              <Button
                                size="sm"
                                disabled={p.nivelPendiente === 2 && p.nivel1UsuarioId === usuarioId}
                                title={
                                  p.nivelPendiente === 2 && p.nivel1UsuarioId === usuarioId
                                    ? 'Firmaste el nivel 1; el nivel 2 lo firma otra persona.'
                                    : undefined
                                }
                                onClick={() => setAFirmar(p)}
                              >
                                {p.estado === 'Rechazado' ? 'Volver a autorizar nivel 1' : `Autorizar nivel ${p.nivelPendiente}`}
                              </Button>
                            )}
                            {p.puedoRechazar && (
                              <Button size="sm" variant="outline" onClick={() => setARechazar(p)}>
                                Rechazar
                              </Button>
                            )}
                          </div>
                        )}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </TableContainer>
        </div>
      )}

      <ConfirmarAccionDialog
        open={aFirmar != null}
        onOpenChange={(o) => !o && setAFirmar(null)}
        titulo="Autorizar pago de comercio exterior"
        resumen={
          aFirmar && (
            <>
              Autorizar el nivel {aFirmar.nivelPendiente ?? 1} del pago de{' '}
              <MoneyDisplay amount={aFirmar.saldoPendiente} currency={aFirmar.moneda} strong /> a{' '}
              {aFirmar.proveedorRazonSocial ?? 'el proveedor'}. Quien firma el nivel 1 no puede firmar el nivel 2.
            </>
          )
        }
        verbo="Autorizar"
        tono="primario"
        onConfirmar={() => aFirmar && ejecutarFirma(aFirmar)}
        pendiente={autorizar.isPending}
      />
      <MotivoDialog
        open={aRechazar != null}
        onOpenChange={(o) => !o && setARechazar(null)}
        titulo="Rechazar pago de comercio exterior"
        descripcion="El pasivo no entra a la corrida hasta que el nivel 1 lo vuelva a autorizar. Indica el motivo."
        confirmLabel="Rechazar"
        onConfirm={(motivo) => aRechazar && ejecutarRechazo(aRechazar, motivo)}
        pending={rechazar.isPending}
      />
    </div>
  );
}
