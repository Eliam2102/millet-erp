import { BarraFiltros, PaginacionTabla, TabsEstado } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { Input } from '@/components/ui/input';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { ESTADOS_CATALOGO } from '../lib/estados-ui';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { useState } from 'react';
import { Plus } from 'lucide-react';
import {
  Dialog,
  DialogContent,
  DialogBody,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { etiquetaNivel } from '@/features/centros-costo/lib/etiquetas';
import { useListaCatalogo } from '@/features/centros-costo/api/useCatalogoCrud';
import { EstatusCatalogo, type GrupoDimDetalle } from '@/features/centros-costo/api/types';
import { GrupoDimDialog, type GrupoDimDialogModo } from './dialogs/GrupoDimDialog';
import {
  ConfirmarEstatusDialog,
  type ConfirmarEstatusTarget,
} from './dialogs/ConfirmarEstatusDialog';

/**
 * CRUD de grupos de clasificación (05 §4.1 — "menú secundario" de la
 * pantalla de configuración): lista plana del recurso con alta, edición
 * y desactivar/reactivar (los grupos NO cascadan). Un solo manager
 * parametrizado — grupos-dim2 y grupos-dim3 comparten forma.
 */
interface GruposManagerDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  recurso: 'grupos-dim2' | 'grupos-dim3';
}

export function GruposManagerDialog({ open, onOpenChange, recurso }: GruposManagerDialogProps) {
  const etiqueta = etiquetaNivel(
    recurso === 'grupos-dim2' ? 'grupoDim2' : 'grupoDim3',
    'configuracion',
  );
  const lista = useListaCatalogo<GrupoDimDetalle>(recurso, { limit: 100 }, { enabled: open });

  const [formulario, setFormulario] = useState<GrupoDimDialogModo | null>(null);
  const [confirmar, setConfirmar] = useState<{
    target: ConfirmarEstatusTarget;
    accion: 'desactivar' | 'reactivar';
  } | null>(null);

  const [estado, setEstado] = useState<number | undefined>();
  const [busqueda, setBusqueda] = useState('');
  const items = (lista.data?.items ?? []).filter(
    (g) =>
      (estado === undefined || g.estatus === estado) &&
      g.nombre.toLocaleLowerCase('es-MX').includes(busqueda.toLocaleLowerCase('es-MX')),
  );
  const paginacion = usePaginacionLocal(items, `${recurso}:${estado}:${busqueda}`);

  return (
    <>
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent size="lg">
          <DialogHeader>
            <DialogTitle>Administrar: {etiqueta}</DialogTitle>
            <DialogDescription>
              Los grupos clasifican, no anidan. Desactivar uno solo lo retira de la clasificación
              nueva — no toca lo ya clasificado.
            </DialogDescription>
          </DialogHeader>

          <DialogBody>
            <ListadoModulo
              tabs={
                <TabsEstado
                  opciones={[
                    EstatusCatalogo.Activo,
                    EstatusCatalogo.EnRevision,
                    EstatusCatalogo.Inactivo,
                  ].map((valor) => ({ valor, label: ESTADOS_CATALOGO[valor].texto }))}
                  valor={estado}
                  onChange={setEstado}
                  conteos={undefined}
                />
              }
              filtros={
                <BarraFiltros
                  busqueda={
                    <Input
                      aria-label="Buscar grupos cargados"
                      placeholder="Nombre del grupo"
                      value={busqueda}
                      onChange={(e) => setBusqueda(e.target.value)}
                    />
                  }
                  filtros={
                    <Button size="sm" onClick={() => setFormulario({ tipo: 'crear' })}>
                      <Plus className="mr-1 h-3.5 w-3.5" aria-hidden="true" />
                      Nuevo
                    </Button>
                  }
                  total={`${items.length} grupos cargados`}
                />
              }
              pie={
                <PaginacionTabla
                  total={items.length}
                  offset={paginacion.offset}
                  limit={10}
                  tamanos={[]}
                  onChange={paginacion.onChange}
                />
              }
            >
              <div data-testid="grupos-lista">
                {lista.isLoading ? (
                  <p role="status" className="p-4 text-sm text-ink-muted">
                    Cargando grupos…
                  </p>
                ) : lista.isError ? (
                  <p role="alert" className="p-4 text-sm text-danger-fg">
                    No se pudieron cargar los grupos. Cierra y vuelve a abrir el diálogo para
                    consultarlos.
                  </p>
                ) : (
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Grupo</TableHead>
                        <TableHead>Estado</TableHead>
                        <TableHead className="text-right">Acciones</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {paginacion.visibles.map((g) => (
                        <TableRow key={g.id}>
                          <TableCell>{g.nombre}</TableCell>
                          <TableCell>
                            <Badge variant={ESTADOS_CATALOGO[g.estatus].tono}>
                              {ESTADOS_CATALOGO[g.estatus].texto}
                            </Badge>
                          </TableCell>
                          <TableCell>
                            <div className="flex justify-end gap-1">
                              <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => setFormulario({ tipo: 'editar', id: g.id })}
                              >
                                Editar
                              </Button>
                              <Button
                                variant="ghost"
                                size="sm"
                                onClick={() =>
                                  setConfirmar({
                                    target: {
                                      recurso,
                                      nivel: recurso === 'grupos-dim2' ? 'grupoDim2' : 'grupoDim3',
                                      id: g.id,
                                      nombre: g.nombre,
                                    },
                                    accion:
                                      g.estatus === EstatusCatalogo.Inactivo
                                        ? 'reactivar'
                                        : 'desactivar',
                                  })
                                }
                              >
                                {g.estatus === EstatusCatalogo.Inactivo
                                  ? 'Reactivar'
                                  : 'Desactivar'}
                              </Button>
                            </div>
                          </TableCell>
                        </TableRow>
                      ))}
                      {items.length === 0 && (
                        <TableRow>
                          <TableCell colSpan={3} className="text-ink-muted">
                            Sin grupos registrados.
                          </TableCell>
                        </TableRow>
                      )}
                    </TableBody>
                  </Table>
                )}
              </div>
            </ListadoModulo>
            {lista.data && lista.data.total > lista.data.items.length && (
              <p className="mt-3 text-xs text-ink-muted">
                Se muestran los {lista.data.items.length} grupos cargados de {lista.data.total}. La
                búsqueda y los filtros se aplican a esta selección.
              </p>
            )}
          </DialogBody>
        </DialogContent>
      </Dialog>

      {formulario && (
        <GrupoDimDialog
          open
          onOpenChange={(abierto) => !abierto && setFormulario(null)}
          recurso={recurso}
          modo={formulario}
        />
      )}
      {confirmar && (
        <ConfirmarEstatusDialog
          open
          onOpenChange={(abierto) => !abierto && setConfirmar(null)}
          target={confirmar.target}
          accion={confirmar.accion}
        />
      )}
    </>
  );
}
