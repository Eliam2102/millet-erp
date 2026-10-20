import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { BarraFiltros, PaginacionTabla } from '@/components/erp';
import { usePaginacionLocal } from '../../lib/use-paginacion-local';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useState } from 'react';
import { Search } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import {
  useAsignarSucursalUbicacion,
  useCentrosCorporativos,
  useMarcarCorporativo,
  useSucursalesContables,
  useUbicacionesSucursal,
} from '../../api/dimensiones';
import { ETIQUETA_DIMENSION, mensajeError } from '../../lib/dimensiones';
import { SELECT_CLASS } from '../../lib/estilos';

/**
 * K10.2 / V49: cada ubicación (Dimensión 1) se liga a una sucursal y sus centros solo se usan en esa sucursal; los CeCo
 * corporativos (Dimensión 2) se usan desde cualquier sucursal. Supuesto hasta la sesión contable del 16-oct.
 */
export function UbicacionesTab({ puedeAdministrar }: { puedeAdministrar: boolean }) {
  return (
    <div className="flex flex-col gap-6">
      <p
        role="note"
        className="rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg"
      >
        Una ubicación sin sucursal deja sus centros fuera de los movimientos contables. La
        equivalencia ubicación → sucursal y los centros corporativos son un supuesto hasta que
        Contabilidad de Millet los confirme.
      </p>
      <Ubicaciones puedeAdministrar={puedeAdministrar} />
      <Corporativos puedeAdministrar={puedeAdministrar} />
    </div>
  );
}

function Ubicaciones({ puedeAdministrar }: { puedeAdministrar: boolean }) {
  const ubicaciones = useUbicacionesSucursal();
  const sucursales = useSucursalesContables();
  const asignar = useAsignarSucursalUbicacion();
  const pagina = usePaginacionLocal(ubicaciones.data ?? [], '');
  const [error, setError] = useState('');

  return (
    <section aria-label="Ubicaciones y su sucursal" className="flex flex-col gap-2">
      <h2 className="text-md font-semibold">{ETIQUETA_DIMENSION.Dim1}: ubicación → sucursal</h2>
      {error && (
        <p role="alert" className="text-sm text-danger-fg">
          {error}
        </p>
      )}
      <ListadoModulo
        filtros={<BarraFiltros total={`${ubicaciones.data?.length ?? 0} registros`} />}
        pie={
          <PaginacionTabla
            total={ubicaciones.data?.length ?? 0}
            offset={pagina.offset}
            limit={10}
            tamanos={[]}
            onChange={pagina.onChange}
          />
        }
      >
        {ubicaciones.isLoading ? (
          <div className="p-4" aria-label="Cargando ubicaciones">
            <Skeleton className="h-8 w-full" />
          </div>
        ) : ubicaciones.isError ? (
          <p role="alert" className="p-4 text-sm text-danger-fg">
            No se pudieron cargar las ubicaciones.
          </p>
        ) : (
          <Table className="w-full text-sm">
            <TableHeader className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
              <TableRow>
                <TableHead className="px-3 py-2">Ubicación</TableHead>
                <TableHead className="px-3 py-2">Sucursal</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {pagina.visibles.map((u) => (
                <TableRow key={u.dim1Id} className="border-b border-line-row">
                  <TableCell className="px-3 py-2">
                    <span className="font-mono text-xs">{u.clave}</span> {u.nombre}
                    {!u.activo && (
                      <Badge variant="neutral" className="ml-2">
                        Dada de baja
                      </Badge>
                    )}
                  </TableCell>
                  <TableCell className="px-3 py-2">
                    {puedeAdministrar ? (
                      <select
                        aria-label={`Sucursal de la ubicación ${u.clave}`}
                        className={SELECT_CLASS}
                        value={u.sucursal?.id ?? ''}
                        disabled={asignar.isPending}
                        onChange={(e) => {
                          setError('');
                          asignar.mutate(
                            { dim1Id: u.dim1Id, sucursalId: e.target.value || null },
                            { onError: (x) => setError(mensajeError(x)) },
                          );
                        }}
                      >
                        <option value="">Sin sucursal</option>
                        {(sucursales.data ?? []).map((s) => (
                          <option key={s.id} value={s.id}>
                            {s.clave} — {s.nombre}
                          </option>
                        ))}
                      </select>
                    ) : u.sucursal ? (
                      <span>
                        {u.sucursal.clave} — {u.sucursal.nombre}
                      </span>
                    ) : (
                      <Badge variant="warning">Sin sucursal</Badge>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </ListadoModulo>
    </section>
  );
}

function Corporativos({ puedeAdministrar }: { puedeAdministrar: boolean }) {
  const [busqueda, setBusqueda] = useState('');
  const [solo, setSolo] = useState(false);
  const q = useDebouncedValue(busqueda.trim(), 200);
  const centros = useCentrosCorporativos(q, solo);
  const marcar = useMarcarCorporativo();
  const pagina = usePaginacionLocal(centros.data ?? [], q + solo);
  const [error, setError] = useState('');

  return (
    <section aria-label="Centros corporativos" className="flex flex-col gap-2">
      <h2 className="text-md font-semibold">Centros corporativos ({ETIQUETA_DIMENSION.Dim2})</h2>
      <p className="text-xs text-ink-muted">
        Dan servicio a toda la empresa y se pueden usar desde cualquier sucursal (p. ej.
        Administración y Finanzas, Abasto y Logística).
      </p>

      {error && (
        <p role="alert" className="text-sm text-danger-fg">
          {error}
        </p>
      )}
      <ListadoModulo
        filtros={
          <BarraFiltros
            filtros={
              <>
                <div className="relative">
                  <Search
                    className="pointer-events-none absolute left-2.5 top-2.5 size-4 text-ink-muted"
                    aria-hidden="true"
                  />
                  <Input
                    aria-label="Buscar centro"
                    placeholder="Clave o nombre del CeCo…"
                    className="w-72 pl-8"
                    value={busqueda}
                    onChange={(e) => setBusqueda(e.target.value)}
                  />
                </div>
                <div className="flex items-center gap-2">
                  <Checkbox
                    id="f-solo-corp"
                    checked={solo}
                    onCheckedChange={(v) => setSolo(v === true)}
                  />
                  <Label htmlFor="f-solo-corp" className="font-normal">
                    Solo corporativos
                  </Label>
                </div>
              </>
            }
            total={`${centros.data?.length ?? 0} registros`}
          />
        }
        pie={
          <PaginacionTabla
            total={centros.data?.length ?? 0}
            offset={pagina.offset}
            limit={10}
            tamanos={[]}
            onChange={pagina.onChange}
          />
        }
      >
        {centros.isLoading ? (
          <div className="p-4" aria-label="Cargando centros">
            <Skeleton className="h-8 w-full" />
          </div>
        ) : centros.isError ? (
          <div role="alert" className="flex items-center gap-2 p-4 text-sm text-danger-fg">
            No se pudieron cargar los centros.
            <Button variant="ghost" size="sm" onClick={() => void centros.refetch()}>
              Reintentar
            </Button>
          </div>
        ) : centros.data!.length === 0 ? (
          <p className="p-6 text-sm text-ink-muted">
            {solo ? 'Aún no hay centros corporativos.' : 'Ningún centro coincide con la búsqueda.'}
          </p>
        ) : (
          <div className="max-h-[50vh] overflow-auto">
            <Table className="w-full text-sm">
              <TableHeader className="sticky top-0 border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
                <TableRow>
                  <TableHead className="px-3 py-2">CeCo</TableHead>
                  <TableHead className="px-3 py-2">Ubicación</TableHead>
                  <TableHead className="px-3 py-2">Corporativo</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {pagina.visibles.map((c) => (
                  <TableRow key={c.dim2Id} className="border-b border-line-row">
                    <TableCell className="px-3 py-2">
                      <span className="font-mono text-xs">{c.clave}</span> {c.nombre}
                      {!c.activo && (
                        <Badge variant="neutral" className="ml-2">
                          Dado de baja
                        </Badge>
                      )}
                    </TableCell>
                    <TableCell className="px-3 py-2 font-mono text-xs">{c.dim1Clave}</TableCell>
                    <TableCell className="px-3 py-2">
                      {puedeAdministrar ? (
                        <Checkbox
                          aria-label={`Corporativo ${c.clave}`}
                          checked={c.corporativo}
                          disabled={marcar.isPending}
                          onCheckedChange={(v) => {
                            setError('');
                            marcar.mutate(
                              { dim2Id: c.dim2Id, corporativo: v === true },
                              { onError: (x) => setError(mensajeError(x)) },
                            );
                          }}
                        />
                      ) : c.corporativo ? (
                        <Badge variant="info">Corporativo</Badge>
                      ) : (
                        <span className="text-ink-muted">—</span>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>
    </section>
  );
}
