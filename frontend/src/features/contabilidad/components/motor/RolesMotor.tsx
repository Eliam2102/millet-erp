import { useState } from 'react';
import { History, Pencil } from 'lucide-react';
import { BarraFiltros, PaginacionTabla } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useSucursalesContables } from '../../api/dimensiones';
import { useRolesCuenta } from '../../api/motor';
import type { RolCuenta } from '../../api/motor-types';
import { fechaCorta } from '../../lib/dimensiones';
import { mensajeMotor } from '../../lib/motor';
import { usePaginacionLocal } from '../../lib/use-paginacion-local';
import { BitacoraRolSheet } from './BitacoraRolSheet';
import { CambiarCuentaRolSheet } from './CambiarCuentaRolSheet';

interface Props {
  puedeAdministrar: boolean;
}

/** Roles de cuenta del motor (C1.4): lo que Contabilidad mantiene para que las plantillas encuentren su cuenta. */
export function RolesMotor({ puedeAdministrar }: Props) {
  const roles = useRolesCuenta();
  // Los nombres de sucursal salen del catálogo de dimensiones: solo se consultan con su permiso (sin él, «Sucursal específica»).
  const puedeLeerSucursales = useHasPermission(PermisosCanonicos.ContabilidadDimensionesLeer);
  const sucursales = useSucursalesContables({ enabled: puedeLeerSucursales });
  const [cambiando, setCambiando] = useState<RolCuenta | null>(null);
  const [bitacora, setBitacora] = useState<RolCuenta | null>(null);
  const items = roles.data ?? [];
  const pagina = usePaginacionLocal(items, '');
  const sinCuenta = items.filter((r) => !r.cuentaId).length;

  function nombreSucursal(id: string): string {
    const s = sucursales.data?.find((x) => x.id === id);
    return s ? `${s.clave} · ${s.nombre}` : 'Sucursal específica';
  }

  return (
    <>
      <ListadoModulo
        filtros={
          <BarraFiltros
            total={`${items.length} roles${sinCuenta > 0 ? ` · ${sinCuenta} sin cuenta` : ''}`}
          />
        }
        pie={
          <PaginacionTabla total={items.length} offset={pagina.offset} limit={10} tamanos={[]} onChange={pagina.onChange} />
        }
      >
        {roles.isLoading ? (
          <div className="flex flex-col gap-2 p-4" aria-label="Cargando roles">
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
          </div>
        ) : roles.isError ? (
          <div role="alert" className="flex items-center gap-2 p-4 text-sm text-danger-fg">
            No se pudieron cargar los roles. {mensajeMotor(roles.error)}
            <Button variant="ghost" size="sm" onClick={() => void roles.refetch()}>
              Reintentar
            </Button>
          </div>
        ) : items.length === 0 ? (
          <p className="p-6 text-sm text-ink-muted">Aún no hay roles de cuenta.</p>
        ) : (
          <div className="overflow-x-auto">
            <Table className="w-full min-w-245 text-sm">
              <TableHeader className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
                <TableRow>
                  <TableHead className="h-9 px-3">Rol</TableHead>
                  <TableHead className="px-3">Cuenta</TableHead>
                  <TableHead className="px-3">Aplica a</TableHead>
                  <TableHead className="px-3">Vigencia</TableHead>
                  <TableHead className="px-3 text-right">Acciones</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {pagina.visibles.map((r) => (
                  <TableRow key={r.id} className="h-12 border-b border-line-row">
                    <TableCell className="max-w-90 px-3">
                      <div className="font-mono text-xs font-medium">{r.codigo}</div>
                      <div className="truncate text-xs text-ink-muted">{r.descripcion}</div>
                    </TableCell>
                    <TableCell className="px-3">
                      {r.cuentaId ? (
                        <span>
                          <span className="font-mono text-xs">{r.cuentaCodigo}</span> {r.cuentaNombre}
                        </span>
                      ) : (
                        <Badge variant="warning">Sin cuenta</Badge>
                      )}
                    </TableCell>
                    <TableCell className="px-3 text-ink-secondary">
                      {[r.sucursalId ? nombreSucursal(r.sucursalId) : 'Todas las sucursales', r.moneda ?? 'todas las monedas'].join(' · ')}
                    </TableCell>
                    <TableCell className="whitespace-nowrap px-3 text-ink-secondary">
                      {r.vigenteHasta
                        ? `${fechaCorta(r.vigenteDesde)} – ${fechaCorta(r.vigenteHasta)}`
                        : `Desde ${fechaCorta(r.vigenteDesde)}`}
                    </TableCell>
                    <TableCell className="px-3">
                      <div className="flex justify-end gap-1" data-print="hidden">
                        {puedeAdministrar && (
                          <Button variant="secondary" size="sm" onClick={() => setCambiando(r)} aria-label={`Cambiar cuenta de ${r.codigo}`}>
                            <Pencil aria-hidden="true" />
                            Cambiar cuenta
                          </Button>
                        )}
                        <Button variant="ghost" size="sm" onClick={() => setBitacora(r)} aria-label={`Bitácora de ${r.codigo}`}>
                          <History aria-hidden="true" />
                          Bitácora
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>

      <CambiarCuentaRolSheet rol={cambiando} onClose={() => setCambiando(null)} />
      <BitacoraRolSheet rol={bitacora} onClose={() => setBitacora(null)} />
    </>
  );
}
