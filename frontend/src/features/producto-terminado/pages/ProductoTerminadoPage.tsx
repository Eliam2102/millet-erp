import { useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import {
  PageHeader,
  EmptyState,
  ErrorState,
  TableSkeleton,
  BarraFiltros,
  PaginacionTabla,
} from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { useSucursales } from '@/features/catalogos/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { usePtList, useResolveScan, type PtPiece, type PtScan } from '../api/hooks';
import { ptSearchSchema, type PtSearch } from '../lib/search';

const reasons: Record<string, string> = {
  PT_PEDIDO_INEXISTENTE: 'El pedido aún no está registrado.',
  PT_PEDIDO_CANCELADO: 'El pedido está cancelado.',
  PT_POSICION_INEXISTENTE: 'La partida no existe en el pedido.',
  PT_PIEZA_EXCEDE_POSICION: 'El número de pieza supera la cantidad de la partida.',
  PT_SUCURSAL_SIN_CLAVE_AW: 'La estación no tiene una sucursal asignada.',
  PT_IDENTIDAD_INCOMPLETA: 'Falta el pedido, la partida o el número de pieza.',
};
export function ProductoTerminadoPage({ exceptions = false }: { exceptions?: boolean }) {
  const search = ptSearchSchema.parse(useSearch({ strict: false }));
  const navigate = useNavigate();
  const route = exceptions ? '/producto-terminado/excepciones' : '/producto-terminado/piezas';
  const list = usePtList<PtPiece | PtScan>(exceptions ? 'escaneos' : 'piezas', search);
  const branches = useSucursales();
  const canManage = useHasPermission(P.ProductoTerminadoExcepcionesGestionar);
  const resolve = useResolveScan();
  const [selected, setSelected] = useState<{
    scan: PtScan;
    action: 'reintentar' | 'descartar';
  } | null>(null);
  const [reason, setReason] = useState('');
  const filter = (patch: Partial<PtSearch>) => {
    void navigate({ to: route, search: { ...search, page: 1, ...patch } });
  };
  const branchName = (id: string | null) =>
    branches.data?.items.find((b) => b.id === id)?.nombre ?? 'Sin sucursal';
  return (
    <main className="flex h-full min-h-0 flex-col gap-5 text-sm">
      <PageHeader
        titulo={exceptions ? 'Escaneos con excepción' : 'Piezas en producto terminado'}
        descripcion={
          exceptions
            ? 'Revisa los escaneos pendientes y resuelve su ingreso con un motivo.'
            : 'Consulta las piezas ingresadas por pedido y sucursal.'
        }
      />
      <ListadoModulo
        filtros={
          <BarraFiltros
            busqueda={
              <label className="flex items-center gap-2 text-xs text-ink-secondary">
                Pedido
                <Input
                  className="h-ctl-md w-64"
                  value={search.pedido ?? ''}
                  placeholder="Número de pedido"
                  onChange={(e) => filter({ pedido: e.target.value || undefined })}
                />
              </label>
            }
            filtros={
              <div className="flex items-center gap-2">
                <SucursalSelector
                  value={search.sucursalId}
                  onChange={(id) => filter({ sucursalId: id ?? undefined })}
                  placeholder="Sucursales permitidas"
                />
                <Button variant="ghost" size="sm" onClick={() => filter({ sucursalId: undefined })}>
                  Limpiar sucursal
                </Button>
              </div>
            }
          />
        }
        pie={
          <PaginacionTabla
            total={list.data?.total ?? 0}
            offset={(search.page - 1) * 10}
            limit={10}
            tamanos={[]}
            onChange={(offset) => filter({ page: offset / 10 + 1 })}
          />
        }
      >
        {list.isLoading ? (
          <TableSkeleton columns={Array.from({ length: 7 }, () => ({ width: 'w-24' }))} rows={10} />
        ) : list.isError ? (
          <ErrorState
            problem={esApiError(list.error) ? list.error.problem : undefined}
            onRetry={() => void list.refetch()}
          />
        ) : !list.data?.items.length ? (
          <EmptyState
            title="Sin registros"
            description="No hay escaneos que coincidan con estos filtros."
          />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Pedido</TableHead>
                <TableHead>Partida</TableHead>
                <TableHead>Pieza</TableHead>
                <TableHead>Sucursal</TableHead>
                <TableHead>{exceptions ? 'Motivo' : 'Hora de escaneo'}</TableHead>
                <TableHead>Estado</TableHead>
                {exceptions && (
                  <>
                    <TableHead>Intentos</TableHead>
                    <TableHead>Acciones</TableHead>
                  </>
                )}
              </TableRow>
            </TableHeader>
            <TableBody>
              {list.data.items.map((item) => (
                <TableRow key={item.id}>
                  <TableCell className="font-mono text-xs">
                    {'pedido' in item ? item.pedido : item.numeroPedido}
                  </TableCell>
                  <TableCell>{item.posicion}</TableCell>
                  <TableCell>{item.numeroPieza ?? '—'}</TableCell>
                  <TableCell>{branchName(item.sucursalId)}</TableCell>
                  {'motivo' in item ? (
                    <>
                      <TableCell>
                        {reasons[item.motivo] ?? 'Revisa los datos del escaneo.'}
                      </TableCell>
                      <TableCell>
                        <Badge variant={item.resultado === 'Pospuesto' ? 'warning' : 'danger'}>
                          {item.resultado === 'Pospuesto' ? 'Pospuesto' : 'Excepción'}
                        </Badge>
                      </TableCell>
                      <TableCell>{item.intentos}</TableCell>
                      <TableCell>
                        {canManage && (
                          <div className="flex gap-2">
                            {(['reintentar', 'descartar'] as const).map((action) => (
                              <Button
                                key={action}
                                variant="secondary"
                                size="sm"
                                onClick={() => {
                                  resolve.reset();
                                  setReason('');
                                  setSelected({ scan: item, action });
                                }}
                              >
                                {action === 'reintentar' ? 'Reintentar' : 'Descartar'}
                              </Button>
                            ))}
                          </div>
                        )}
                      </TableCell>
                    </>
                  ) : (
                    <>
                      <TableCell>{new Date(item.escaneadaAt).toLocaleString('es-MX')}</TableCell>
                      <TableCell>
                        <Badge variant="success">En PT · {item.estatusAw}</Badge>
                      </TableCell>
                    </>
                  )}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </ListadoModulo>
      <Dialog
        open={selected != null}
        onOpenChange={(open) => {
          if (!open && !resolve.isPending) setSelected(null);
        }}
      >
        <DialogContent size="sm">
          <DialogHeader>
            <DialogTitle>
              {selected?.action === 'descartar' ? 'Descartar escaneo' : 'Reintentar escaneo'}
            </DialogTitle>
            <DialogDescription>
              El motivo y tu usuario quedarán registrados en el escaneo.
            </DialogDescription>
          </DialogHeader>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              if (selected)
                resolve.mutate({ ...selected, reason }, { onSuccess: () => setSelected(null) });
            }}
          >
            <DialogBody>
              <label className="flex flex-col gap-1.5 text-xs font-medium text-ink-strong">
                Motivo *
                <Input
                  required
                  maxLength={500}
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                />
              </label>
              {resolve.isError && (
                <ErrorState
                  problem={esApiError(resolve.error) ? resolve.error.problem : undefined}
                />
              )}
            </DialogBody>
            <DialogFooter>
              <Button
                type="button"
                variant="ghost"
                onClick={() => setSelected(null)}
                disabled={resolve.isPending}
                title={resolve.isPending ? 'Espera a que termine la operación.' : undefined}
              >
                Cancelar
              </Button>
              <Button
                type="submit"
                disabled={!reason.trim() || resolve.isPending}
                title={
                  !reason.trim()
                    ? 'Captura el motivo para continuar.'
                    : resolve.isPending
                      ? 'Guardando el resultado.'
                      : undefined
                }
              >
                {selected?.action === 'descartar' ? 'Descartar' : 'Reintentar'}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </main>
  );
}
