import { useId, useState } from 'react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from '@/components/ui/dialog';
import { Skeleton } from '@/components/ui/skeleton';
import { Textarea } from '@/components/ui/textarea';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { PaginacionTabla } from '@/components/erp';
import { esApiError } from '@/lib/api';
import { formatDate } from '@/lib/datetime';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import {
  PERMISO_UNIFICAR_CLIENTES,
  useRfcRepetidos,
  useUnificarClientesRfc,
  type ClienteRfcRepetido,
  type GrupoRfcRepetido,
} from '@/modules/datos-maestros/api/sincronizacion-aw';

const POR_PAGINA = 10;
const MOTIVO_MIN = 5;
const MOTIVO_MAX = 300;

function accionDe(c: ClienteRfcRepetido) {
  return c.pedidosAw === 0 && c.pedidosErp === 0 ? 'Fusionar' : 'Agrupar';
}

function mensajeError(e: unknown) {
  return esApiError(e) ? (e.problem.detail ?? e.problem.title) : 'No se pudo unificar. Reintenta.';
}

function GrupoCard({ grupo }: { grupo: GrupoRfcRepetido }) {
  const puede = useHasPermission(PERMISO_UNIFICAR_CLIENTES);
  const unificar = useUnificarClientesRfc();
  const [principalId, setPrincipalId] = useState(grupo.principalSugeridoId);
  const [abierto, setAbierto] = useState(false);
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [resultado, setResultado] = useState<string | null>(null);
  const motivoId = useId();
  const nombre = `rfc-principal-${grupo.rfc}`;
  const otros = grupo.clientes.filter((c) => c.clienteId !== principalId && c.unificadoEnClienteId == null);
  const principal = grupo.clientes.find((c) => c.clienteId === principalId);
  const motivoLimpio = motivo.trim();
  const motivoValido = motivoLimpio.length >= MOTIVO_MIN && motivoLimpio.length <= MOTIVO_MAX;
  const sinPendientes = grupo.resuelto || otros.length === 0;

  function confirmar() {
    setError(null);
    unificar.mutate(
      { rfc: grupo.rfc, principalClienteId: principalId, motivo: motivoLimpio },
      {
        onSuccess: () => {
          toast.success(`RFC ${grupo.rfc} unificado`);
          setResultado(`RFC ${grupo.rfc} unificado en ${principal?.razonSocial ?? 'el principal'}.`);
          setAbierto(false);
          setMotivo('');
        },
        onError: (e) => setError(mensajeError(e)),
      },
    );
  }

  return (
    <article className="space-y-3 rounded-md border border-line p-3" aria-label={`RFC ${grupo.rfc}`}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="font-mono text-base font-semibold">{grupo.rfc}</h3>
          <p className="text-xs text-ink-muted">Principal sugerido: {grupo.motivoSugerencia}</p>
        </div>
        {grupo.resuelto ? (
          <Badge variant="success">Resuelto</Badge>
        ) : (
          <Button
            size="sm"
            disabled={!puede || sinPendientes}
            title={puede ? undefined : 'Necesitas el permiso de unificar clientes.'}
            onClick={() => { setError(null); setAbierto(true); }}
          >
            Unificar en el principal
          </Button>
        )}
      </div>
      <p role="status" className="text-xs text-success-fg empty:hidden">{resultado}</p>
      <fieldset>
        <legend className="sr-only">{`Cliente principal para el RFC ${grupo.rfc}`}</legend>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Principal</TableHead>
              <TableHead>Ref. A+W</TableHead>
              <TableHead>Razón social</TableHead>
              <TableHead>Pedidos A+W</TableHead>
              <TableHead>Pedidos ERP</TableHead>
              <TableHead>Acción</TableHead>
              <TableHead>Estatus</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {grupo.clientes.map((c) => {
              const unificado = c.unificadoEnClienteId != null;
              const accion = c.clienteId === principalId ? 'Principal' : accionDe(c);
              return (
                <TableRow key={c.clienteId}>
                  <TableCell>
                    <input
                      type="radio"
                      name={nombre}
                      className="h-4 w-4 accent-primary"
                      aria-label={`Principal: ${c.razonSocial}`}
                      checked={c.clienteId === principalId}
                      disabled={unificado || grupo.resuelto}
                      onChange={() => setPrincipalId(c.clienteId)}
                    />
                  </TableCell>
                  <TableCell className="font-mono">{c.referenciaAw ?? '—'}</TableCell>
                  <TableCell>{c.razonSocial}</TableCell>
                  <TableCell>
                    {c.pedidosAw}
                    {c.ultimoPedidoAw && (
                      <span className="block text-xs text-ink-muted">Último {formatDate(c.ultimoPedidoAw)}</span>
                    )}
                  </TableCell>
                  <TableCell>{c.pedidosErp}</TableCell>
                  <TableCell>
                    {unificado ? (
                      <Badge variant="neutral">Unificado</Badge>
                    ) : (
                      <Badge variant={accion === 'Principal' ? 'success' : accion === 'Fusionar' ? 'neutral' : 'info'}>
                        {accion}
                      </Badge>
                    )}
                  </TableCell>
                  <TableCell>{c.estatus}</TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </fieldset>

      <Dialog open={abierto} onOpenChange={(o) => !unificar.isPending && setAbierto(o)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{`Unificar RFC ${grupo.rfc}`}</DialogTitle>
            <DialogDescription>
              {`El principal será ${principal?.razonSocial ?? ''}. Los pedidos nuevos de A+W de los demás clientes se facturarán a nombre del principal. A+W no se modifica.`}
            </DialogDescription>
          </DialogHeader>
          <ul className="space-y-2 text-sm">
            {otros.map((c) => (
              <li key={c.clienteId}>
                <strong>{c.razonSocial}</strong>
                {accionDe(c) === 'Fusionar'
                  ? ': se fusiona. No tiene pedidos, así que queda inactivo.'
                  : ': se agrupa. Conserva sus saldos, facturas y pedidos actuales, y sigue activo; los pedidos nuevos van al principal.'}
              </li>
            ))}
          </ul>
          <div className="space-y-1">
            <label htmlFor={motivoId} className="text-sm font-medium">Motivo ({MOTIVO_MIN} a {MOTIVO_MAX} caracteres)</label>
            <Textarea
              id={motivoId}
              value={motivo}
              maxLength={MOTIVO_MAX}
              onChange={(e) => setMotivo(e.target.value)}
            />
          </div>
          {error && <p role="alert" className="text-sm text-danger-fg">{error}</p>}
          <DialogFooter>
            <Button variant="ghost" disabled={unificar.isPending} onClick={() => setAbierto(false)}>Cancelar</Button>
            <Button disabled={!motivoValido || unificar.isPending} aria-busy={unificar.isPending} onClick={confirmar}>
              {unificar.isPending ? 'Unificando…' : 'Confirmar unificación'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </article>
  );
}

/** Grupos de clientes de A+W que comparten RFC, con sugerencia de principal y unificación. */
export function RfcRepetidosSeccion({ enabled = true }: { enabled?: boolean }) {
  const [soloPendientes, setSoloPendientes] = useState(true);
  const [offset, setOffset] = useState(0);
  const q = useRfcRepetidos({ offset, limit: POR_PAGINA, soloPendientes }, enabled);
  const total = q.data?.total ?? 0;

  return (
    <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat" aria-label="RFC repetidos">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-lg font-semibold">{`RFC repetidos${q.data ? ` (${total})` : ''}`}</h2>
        <label className="flex items-center gap-2 text-xs">
          <input
            type="checkbox"
            className="h-4 w-4 accent-primary"
            checked={soloPendientes}
            onChange={(e) => { setSoloPendientes(e.target.checked); setOffset(0); }}
          />
          Solo pendientes
        </label>
      </div>
      {q.isLoading && (
        <div aria-busy="true" aria-label="Cargando RFC repetidos" className="space-y-2">
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-24 w-full" />
        </div>
      )}
      {q.isError && (
        <div role="alert" className="flex items-center gap-3 text-danger-fg">
          <span>No se pudieron cargar los RFC repetidos.</span>
          <Button size="sm" variant="secondary" onClick={() => void q.refetch()}>Reintentar</Button>
        </div>
      )}
      {q.data && q.data.items.length === 0 && (
        <p className="py-3 text-ink-muted">
          {soloPendientes ? 'No hay RFC repetidos pendientes.' : 'No hay RFC repetidos.'}
        </p>
      )}
      {q.data?.items.map((g) => <GrupoCard key={g.rfc} grupo={g} />)}
      {total > POR_PAGINA && (
        <PaginacionTabla total={total} offset={offset} limit={POR_PAGINA} tamanos={[]} onChange={(o) => setOffset(o)} />
      )}
    </section>
  );
}
