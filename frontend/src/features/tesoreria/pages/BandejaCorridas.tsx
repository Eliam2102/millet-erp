import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Inbox, Plus } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  BarraFiltros,
  EmptyState,
  ErrorState,
  PaginacionTabla,
  TableSkeleton,
} from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { useCorridas } from '@/features/tesoreria/api/useTesoreria';
import { ESTADO_CORRIDA_UI } from '@/features/tesoreria/lib/corridas';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';

const TODOS = 'todos';
const LIMIT = 20;

/**
 * <c>Corridas de pago</c> (T9.1-A): borrador → en autorización → autorizada.
 * El oficio y el registro de la ejecución llegan en la Parte B (86e3jvvhf).
 */
export function BandejaCorridas() {
  const [estado, setEstado] = useState<string>(TODOS);
  const [offset, setOffset] = useState(0);
  const puedeCrear = useHasPermission(PermisosCanonicos.TesoreriaCorridasCrear);
  const query = useCorridas({ estado: estado === TODOS ? undefined : estado, offset, limit: LIMIT });
  const items = query.data?.items ?? [];

  return (
    <div className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        titulo="Corridas de pago"
        descripcion="Arma la corrida con los pasivos autorizados por Cuentas por Pagar, envíala a autorización y, con las firmas requeridas, queda lista para pagar."
        acciones={
          puedeCrear ? (
            <Button asChild size="sm">
              <Link to="/tesoreria/corridas/nueva">
                <Plus className="mr-1 h-3.5 w-3.5" aria-hidden="true" />
                Nueva corrida
              </Link>
            </Button>
          ) : undefined
        }
      />
      <ListadoModulo
        filtros={
          <BarraFiltros
            aria-label="Filtros de corridas"
            filtros={
              <Select
                value={estado}
                onValueChange={(v) => {
                  setEstado(v);
                  setOffset(0);
                }}
              >
                <SelectTrigger className="h-ctl-md w-48 text-xs" aria-label="Estado">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={TODOS}>Todos los estados</SelectItem>
                  {Object.entries(ESTADO_CORRIDA_UI).map(([valor, ui]) => (
                    <SelectItem key={valor} value={valor}>
                      {ui.texto}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            }
            total={query.data ? `${query.data.total} corridas` : undefined}
          />
        }
        pie={
          <PaginacionTabla
            total={query.data?.total ?? 0}
            offset={query.data?.offset ?? offset}
            limit={query.data?.limit ?? LIMIT}
            onChange={(o) => setOffset(o)}
          />
        }
      >
        {query.isError ? (
          <ErrorState
            title="No se pudieron cargar las corridas"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton rows={6} columns={[{ width: 'w-56' }, { width: 'w-28' }, { width: 'w-28' }, { width: 'w-24' }, { width: 'w-24' }]} />
        ) : items.length === 0 ? (
          <EmptyState
            icon={<Inbox className="h-10 w-10" />}
            title="Sin corridas"
            description={estado === TODOS ? 'Arma la primera con «Nueva corrida».' : 'No hay corridas en ese estado.'}
          />
        ) : (
          <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
            <TableContainer>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Corrida</TableHead>
                    <TableHead>Estado</TableHead>
                    <TableHead className="text-right">Pasivos</TableHead>
                    <TableHead className="text-right">Total a transferir</TableHead>
                    <TableHead>Firmas</TableHead>
                    <TableHead>Creada</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((c) => {
                    const ui = ESTADO_CORRIDA_UI[c.estado];
                    return (
                      <TableRow key={c.id} className="hover:bg-surface-subtle">
                        <TableCell>
                          <Link
                            to="/tesoreria/corridas/$id"
                            params={{ id: c.id }}
                            className="text-brand hover:underline"
                            aria-label={`Abrir corrida ${c.nombre}`}
                          >
                            {c.nombre}
                          </Link>
                        </TableCell>
                        <TableCell>
                          <Badge variant={ui.tono}>{ui.texto}</Badge>
                        </TableCell>
                        <TableCell className="text-right tabular-nums">{c.pasivos}</TableCell>
                        <TableCell className="text-right">
                          <MoneyDisplay amount={c.total} currency={c.moneda} strong />
                        </TableCell>
                        <TableCell className="text-xs tabular-nums">
                          {c.estado === 'EnAutorizacion' || c.estado === 'Autorizada'
                            ? `${c.firmasRecibidas} de ${c.firmasRequeridas}`
                            : '—'}
                        </TableCell>
                        <TableCell className="text-xs">
                          <DateTimeDisplay value={c.creadaEn} variant="corta" />
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </TableContainer>
          </div>
        )}
      </ListadoModulo>
    </div>
  );
}
