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
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { MotivoDialog } from '../components/MotivoDialog';
import {
  ESTADO_DIFERENCIA_CORTE_LABELS,
  EstadoDiferenciaCorte,
  useAutorizarDiferenciaCorte,
  useDiferenciasCorte,
  useRechazarDiferenciaCorte,
  type DiferenciaCorteResponse,
} from '../api/useDiferenciasCorte';

const TONO: Record<EstadoDiferenciaCorte, 'warning' | 'info' | 'danger' | 'success'> = {
  [EstadoDiferenciaCorte.PendienteAutorizar]: 'warning',
  [EstadoDiferenciaCorte.Autorizada]: 'info',
  [EstadoDiferenciaCorte.PorAclarar]: 'danger',
  [EstadoDiferenciaCorte.Saldada]: 'success',
  [EstadoDiferenciaCorte.SaldadaParcial]: 'info',
};

/**
 * <c>Diferencias de corte</c> (86e3vr1tj, Millet 23-oct): Tesorería autoriza o rechaza el faltante o
 * sobrante de cada corte de caja; va a «deudores funcionarios y empleados». El cajero del corte no
 * puede resolver el suyo (el servidor lo valida; aquí solo se deshabilitan los botones).
 */
export function BandejaDiferenciasCorte() {
  const query = useDiferenciasCorte({ limit: 100 });
  const keyFor = useBodyScopedIdempotencyKey();
  const puede = useHasPermission(PermisosCanonicos.TesoreriaDiferenciasCorteAutorizar);
  const usuarioId = useAuthStore((s) => s.user?.id);
  const autorizar = useAutorizarDiferenciaCorte();
  const rechazar = useRechazarDiferenciaCorte();
  const [aAutorizar, setAAutorizar] = useState<DiferenciaCorteResponse | null>(null);
  const [aRechazar, setARechazar] = useState<DiferenciaCorteResponse | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);

  const items = query.data?.items ?? [];

  function alError(e: unknown, fallback: string) {
    if (esApiError(e)) setAviso(e.problem.detail ?? e.problem.title);
    else toast.error(fallback);
  }

  function ejecutarAutorizar(d: DiferenciaCorteResponse) {
    setAviso(null);
    const body = { id: d.id, versionEsperada: d.version };
    autorizar.mutate(
      { ...body, idempotencyKey: keyFor(body) },
      {
        onSuccess: () => {
          toast.success('Diferencia autorizada');
          setAAutorizar(null);
        },
        onError: (e) => {
          setAAutorizar(null);
          alError(e, 'No se pudo autorizar la diferencia.');
        },
      },
    );
  }

  function ejecutarRechazar(d: DiferenciaCorteResponse, motivo: string) {
    setAviso(null);
    const body = { id: d.id, versionEsperada: d.version, motivo };
    rechazar.mutate(
      { ...body, idempotencyKey: keyFor(body) },
      {
        onSuccess: () => {
          toast.success('Diferencia por aclarar');
          setARechazar(null);
        },
        onError: (e) => {
          setARechazar(null);
          alError(e, 'No se pudo rechazar la diferencia.');
        },
      },
    );
  }

  return (
    <div className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        titulo="Diferencias de corte"
        descripcion="Faltantes y sobrantes del efectivo de cada corte de caja: Tesorería los autoriza y van a deudores funcionarios y empleados."
      />
      {aviso && <AvisoEnLinea tono="error">{aviso}</AvisoEnLinea>}

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las diferencias"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={5}
          columns={[{ width: 'w-24' }, { width: 'w-28' }, { width: 'w-24' }, { width: 'w-24' }, { width: 'w-28' }, { width: 'w-32' }, { width: 'w-32' }]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          icon={<Inbox className="h-10 w-10" />}
          title="Sin diferencias"
          description="Los cortes que cierran con faltante o sobrante aparecen aquí."
        />
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <TableContainer>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Corte</TableHead>
                  <TableHead>Cajero</TableHead>
                  <TableHead>Caja</TableHead>
                  <TableHead>Tipo</TableHead>
                  <TableHead className="text-right">Importe</TableHead>
                  <TableHead className="text-right">Devuelto</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead>Recibida</TableHead>
                  <TableHead className="text-right">Acciones</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((d) => {
                  const propia = d.cajeroId === usuarioId;
                  const resoluble = d.estado === EstadoDiferenciaCorte.PendienteAutorizar;
                  const reautorizable = d.estado === EstadoDiferenciaCorte.PorAclarar;
                  return (
                    <TableRow key={d.id} className="hover:bg-surface-subtle">
                      <TableCell>{d.diaCorte}</TableCell>
                      <TableCell>{d.cajeroNombre ?? '—'}</TableCell>
                      <TableCell>{d.cajaNombre ?? '—'}</TableCell>
                      <TableCell>{d.tipo}</TableCell>
                      <TableCell className="text-right">
                        <MoneyDisplay amount={d.importe} currency="MXN" strong />
                      </TableCell>
                      <TableCell className="text-right">
                        {d.tipo === 'Faltante' ? <MoneyDisplay amount={d.importeDevuelto} currency="MXN" /> : '—'}
                      </TableCell>
                      <TableCell>
                        <Badge variant={TONO[d.estado]}>{ESTADO_DIFERENCIA_CORTE_LABELS[d.estado]}</Badge>
                        {d.motivoRechazo && (
                          <span className="block max-w-48 truncate text-xs text-ink-muted" title={d.motivoRechazo}>
                            {d.motivoRechazo}
                          </span>
                        )}
                      </TableCell>
                      <TableCell className="text-xs">
                        <DateTimeDisplay value={d.recibidoEn} variant="corta" />
                      </TableCell>
                      <TableCell className="text-right">
                        {puede && (resoluble || reautorizable) && (
                          <div className="flex justify-end gap-2">
                            <Button
                              size="sm"
                              disabled={propia}
                              title={propia ? 'No puedes autorizar la diferencia de tu propio corte.' : undefined}
                              onClick={() => setAAutorizar(d)}
                            >
                              Autorizar
                            </Button>
                            {resoluble && (
                              <Button
                                size="sm"
                                variant="outline"
                                disabled={propia}
                                title={propia ? 'No puedes resolver la diferencia de tu propio corte.' : undefined}
                                onClick={() => setARechazar(d)}
                              >
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
        open={aAutorizar != null}
        onOpenChange={(o) => !o && setAAutorizar(null)}
        titulo="Autorizar diferencia de corte"
        resumen={
          aAutorizar && (
            <>
              Autorizar el {aAutorizar.tipo.toLowerCase()} de{' '}
              <MoneyDisplay amount={Math.abs(aAutorizar.importe)} currency="MXN" strong /> del corte del{' '}
              {aAutorizar.diaCorte}. Va a deudores funcionarios y empleados.
            </>
          )
        }
        verbo="Autorizar"
        tono="primario"
        onConfirmar={() => aAutorizar && ejecutarAutorizar(aAutorizar)}
        pendiente={autorizar.isPending}
      />
      <MotivoDialog
        open={aRechazar != null}
        onOpenChange={(o) => !o && setARechazar(null)}
        titulo="Rechazar diferencia de corte"
        descripcion="La diferencia queda «Por aclarar» y el corte no se concilia. Indica el motivo."
        confirmLabel="Rechazar"
        onConfirm={(motivo) => aRechazar && ejecutarRechazar(aRechazar, motivo)}
        pending={rechazar.isPending}
      />
    </div>
  );
}
