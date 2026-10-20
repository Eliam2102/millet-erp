import { useState } from 'react';
import { EmpleadoSelector } from '@/components/erp/selectors/EmpleadoSelector';
import { Link, useSearch, useNavigate } from '@tanstack/react-router';
import { PageHeader } from '@/components/erp/PageHeader';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Dim3Picker } from '@/features/centros-costo/components/Dim3Picker';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useActivos, useClasesActivo } from '../api/hooks';
import { useNuevoActivo } from '../components/nuevo-activo-context';
import { dinero, estadoActivo } from '../api/types';
export function ActivosPage() {
  const [responsableEmpleado, setResponsableEmpleado] = useState(false);
  const filtros = useSearch({ from: '/_app/activos-fijos/activos/' });
  const navigate = useNavigate();
  const query = useActivos(filtros);
  const clases = useClasesActivo();
  const nuevo = useNuevoActivo();
  const gestionar = useHasPermission(P.ActivosFijosActivosGestionar);
  const filtrar = (patch: Partial<typeof filtros>) => {
    void navigate({ to: '/activos-fijos/activos', search: { ...filtros, offset: 0, ...patch } });
  };
  return (
    <main className="flex h-full min-h-0 flex-col gap-5 text-sm">
      <PageHeader
        titulo="Activos fijos"
        descripcion="Maestro, capitalización y custodia de activos."
        acciones={gestionar && <Button onClick={nuevo.abrir}>Alta manual</Button>}
      />
      <ListadoModulo
        filtros={
          <div className="flex flex-wrap items-center gap-3 p-4">
            <label className="text-xs">
              Estado
              <select
                aria-label="Estado"
                className="ml-2 h-ctl-md rounded-md border border-line-control bg-surface-card px-2"
                value={filtros.estado ?? ''}
                onChange={(e) =>
                  filtrar({ estado: e.target.value === '' ? undefined : Number(e.target.value) })
                }
              >
                <option value="">Todos</option>
                {estadoActivo.map((s, i) => (
                  <option key={s} value={i}>
                    {s}
                  </option>
                ))}
              </select>
            </label>
            <label className="text-xs">
              Clase
              <select
                aria-label="Clase"
                className="ml-2 h-ctl-md rounded-md border border-line-control bg-surface-card px-2"
                value={filtros.claseId ?? ''}
                onChange={(e) => filtrar({ claseId: e.target.value || undefined })}
              >
                <option value="">Todas</option>
                {clases.data?.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.nombre}
                  </option>
                ))}
              </select>
            </label>
            <SucursalSelector
              value={filtros.sucursalId}
              onChange={(id) => filtrar({ sucursalId: id ?? undefined })}
            />
            <label className="flex items-center gap-2 text-xs">
              <input
                type="checkbox"
                checked={responsableEmpleado}
                onChange={(e) => {
                  setResponsableEmpleado(e.target.checked);
                  filtrar({ responsableId: undefined });
                }}
              />
              Responsable empleado
            </label>
            {responsableEmpleado ? (
              <EmpleadoSelector
                value={filtros.responsableId}
                onChange={(id) => filtrar({ responsableId: id ?? undefined })}
              />
            ) : (
              <UsuarioSelector
                value={filtros.responsableId}
                onChange={(id) => filtrar({ responsableId: id ?? undefined })}
                placeholder="Responsable"
              />
            )}
            <Dim3Picker
              endpoint="/api/v1/activos-fijos/equipos"
              ariaLabel="Filtrar equipo"
              value={filtros.equipoId ?? null}
              onChange={(id) => filtrar({ equipoId: id ?? undefined })}
            />
          </div>
        }
        pie={
          <div className="flex items-center justify-between p-3 text-xs">
            <span>{query.data?.total ?? 0} activos</span>
            <div className="flex gap-2">
              <Button
                variant="outline"
                disabled={!filtros.offset}
                title={!filtros.offset ? 'Primera página' : undefined}
                onClick={() => filtrar({ offset: Math.max(0, (filtros.offset ?? 0) - 10) })}
              >
                Anterior
              </Button>
              <Button
                variant="outline"
                disabled={(filtros.offset ?? 0) + 10 >= (query.data?.total ?? 0)}
                title="Avanzar si hay más registros"
                onClick={() => filtrar({ offset: (filtros.offset ?? 0) + 10 })}
              >
                Siguiente
              </Button>
            </div>
          </div>
        }
      >
        {query.isPending ? (
          <p className="p-4">Cargando activos…</p>
        ) : query.error ? (
          <p role="alert" className="p-4 text-danger-fg">
            {query.error.message}
          </p>
        ) : query.data?.items.length === 0 ? (
          <p className="p-4 text-ink-muted">No hay activos con estos filtros.</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Número</TableHead>
                <TableHead>Descripción</TableHead>
                <TableHead>Clase</TableHead>
                <TableHead className="text-right">Valor en libros</TableHead>
                <TableHead>Estado</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {query.data?.items.map((a) => (
                <TableRow key={a.id}>
                  <TableCell>
                    <Link
                      className="font-mono text-brand"
                      to="/activos-fijos/activos/$id"
                      params={{ id: a.id }}
                    >
                      {a.numero}
                    </Link>
                  </TableCell>
                  <TableCell>{a.descripcion}</TableCell>
                  <TableCell>
                    {clases.data?.find((c) => c.id === a.claseId)?.nombre ?? 'Sin clase'}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">
                    {dinero(a.valorEnLibros)}
                  </TableCell>
                  <TableCell>
                    <Badge
                      variant={a.estado === 1 ? 'success' : a.estado === 0 ? 'warning' : 'neutral'}
                    >
                      {estadoActivo[a.estado]}
                    </Badge>
                    {a.enTraslado && <Badge variant="info">En traslado</Badge>}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </ListadoModulo>
    </main>
  );
}
