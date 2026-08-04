import { useMemo, useState, type ReactNode } from 'react';
import { Package, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { useArticulos } from '@/modules/datos-maestros/api';
import {
  EstatusCatalogo,
  Naturaleza,
} from '@/modules/datos-maestros/api/types';
import type { ListarArticulosFiltros } from '@/modules/datos-maestros/api/keys';
import { esApiError } from '@/lib/api';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ListaArticulosCompacta } from '@/modules/datos-maestros/components/ListaArticulosCompacta';
import { useNuevoArticulo } from '@/modules/datos-maestros/components/nuevo-articulo-context';
import { cn } from '@/lib/utils';

/**
 * Layout master-detail (P3 del patrón cross-módulo) para
 * <c>/admin/datos-maestros/articulos</c>. Análogo a
 * <see cref="ProveedoresLayout"/>; filtros: código, descripción,
 * naturaleza, unidad de medida, estatus.
 */
export interface ArticulosLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function ArticulosLayout({ idActivo, detalle }: ArticulosLayoutProps) {
  const nuevoArticulo = useNuevoArticulo();
  const canAdministrar = useHasPermission(
    PermisosCanonicos.CompartidoCatalogosAdministrar,
  );

  const [codigoInput, setCodigoInput] = useState('');
  const [descripcionInput, setDescripcionInput] = useState('');
  const [unidadInput, setUnidadInput] = useState('');
  const [naturaleza, setNaturaleza] = useState<string>('');
  const [estatus, setEstatus] = useState<string>('');

  const codigoDeb = useDebouncedValue(codigoInput, 300);
  const descripcionDeb = useDebouncedValue(descripcionInput, 300);
  const unidadDeb = useDebouncedValue(unidadInput, 300);

  const filtros = useMemo<ListarArticulosFiltros>(
    () => ({
      codigo: codigoDeb.trim() || undefined,
      descripcion: descripcionDeb.trim() || undefined,
      unidadMedidaDefault: unidadDeb.trim() || undefined,
      naturaleza:
        naturaleza !== '' ? (Number(naturaleza) as Naturaleza) : undefined,
      estatus:
        estatus !== '' ? (Number(estatus) as EstatusCatalogo) : undefined,
      limit: 200,
    }),
    [codigoDeb, descripcionDeb, unidadDeb, naturaleza, estatus],
  );

  const articulosQuery = useArticulos(filtros);
  const items = useMemo(
    () => articulosQuery.data?.items ?? [],
    [articulosQuery.data],
  );

  return (
    <div className="flex flex-col gap-4 md:h-[calc(100vh-3.5rem-3rem)] md:flex-row">
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de artículos"
        data-print="hidden"
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h1 className="text-xl font-semibold tracking-tight">Artículos</h1>
          {canAdministrar && (
            <Button size="sm" onClick={() => nuevoArticulo.abrir()}>
              <Plus className="mr-1 h-4 w-4" />
              Nuevo
            </Button>
          )}
        </div>

        <FiltrosBloque
          codigo={codigoInput}
          onCodigo={setCodigoInput}
          descripcion={descripcionInput}
          onDescripcion={setDescripcionInput}
          unidad={unidadInput}
          onUnidad={setUnidadInput}
          naturaleza={naturaleza}
          onNaturaleza={setNaturaleza}
          estatus={estatus}
          onEstatus={setEstatus}
        />

        <div className="flex-1 overflow-y-auto rounded-md border bg-card">
          <RenderLista
            query={articulosQuery}
            items={items}
            idActivo={idActivo}
            canCrear={canAdministrar}
            onAbrirNuevo={() => nuevoArticulo.abrir()}
          />
        </div>
      </aside>

      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de artículo"
      >
        {idActivo == null ? <PlaceholderSinSeleccion /> : detalle}
      </section>
    </div>
  );
}

interface FiltrosBloqueProps {
  codigo: string;
  onCodigo: (v: string) => void;
  descripcion: string;
  onDescripcion: (v: string) => void;
  unidad: string;
  onUnidad: (v: string) => void;
  naturaleza: string;
  onNaturaleza: (v: string) => void;
  estatus: string;
  onEstatus: (v: string) => void;
}

function FiltrosBloque({
  codigo,
  onCodigo,
  descripcion,
  onDescripcion,
  unidad,
  onUnidad,
  naturaleza,
  onNaturaleza,
  estatus,
  onEstatus,
}: FiltrosBloqueProps) {
  return (
    <div className="space-y-2 rounded-md border bg-muted/20 p-2">
      <Input
        placeholder="Código…"
        value={codigo}
        onChange={(e) => onCodigo(e.target.value)}
        className="h-8 text-xs"
        aria-label="Filtrar por código"
      />
      <Input
        placeholder="Descripción…"
        value={descripcion}
        onChange={(e) => onDescripcion(e.target.value)}
        className="h-8 text-xs"
        aria-label="Filtrar por descripción"
      />
      <Input
        placeholder="Unidad de medida (PZA, KG, …)"
        value={unidad}
        onChange={(e) => onUnidad(e.target.value)}
        className="h-8 text-xs"
        aria-label="Filtrar por unidad de medida"
      />
      <div className="flex gap-2">
        <Select
          value={naturaleza === '' ? 'all' : naturaleza}
          onValueChange={(v) => onNaturaleza(v === 'all' ? '' : v)}
        >
          <SelectTrigger className="h-8 text-xs" aria-label="Naturaleza">
            <SelectValue placeholder="Naturaleza" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Todas</SelectItem>
            <SelectItem value={String(Naturaleza.Estandar)}>Estándar</SelectItem>
            <SelectItem value={String(Naturaleza.Servicio)}>Servicio</SelectItem>
            <SelectItem value={String(Naturaleza.Critico)}>Crítico</SelectItem>
            <SelectItem value={String(Naturaleza.Riesgo)}>Riesgo</SelectItem>
          </SelectContent>
        </Select>
        <Select
          value={estatus === '' ? 'all' : estatus}
          onValueChange={(v) => onEstatus(v === 'all' ? '' : v)}
        >
          <SelectTrigger className="h-8 text-xs" aria-label="Estatus">
            <SelectValue placeholder="Estatus" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Todos</SelectItem>
            <SelectItem value={String(EstatusCatalogo.Activo)}>
              Activo
            </SelectItem>
            <SelectItem value={String(EstatusCatalogo.Inactivo)}>
              Inactivo
            </SelectItem>
            <SelectItem value={String(EstatusCatalogo.EnRevision)}>
              En revisión
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
    </div>
  );
}

interface RenderListaProps {
  query: ReturnType<typeof useArticulos>;
  items: ReturnType<typeof useArticulos>['data'] extends
    | { items: infer I }
    | undefined
    ? I
    : never;
  idActivo: string | null;
  canCrear: boolean;
  onAbrirNuevo: () => void;
}

function RenderLista({
  query,
  items,
  idActivo,
  canCrear,
  onAbrirNuevo,
}: RenderListaProps) {
  if (query.isLoading) {
    return (
      <div className="p-3">
        <TableSkeleton
          rows={6}
          columns={[{ width: 'w-full' }, { width: 'w-full' }]}
        />
      </div>
    );
  }

  if (query.isError) {
    const problem = esApiError(query.error) ? query.error.problem : undefined;
    return <ErrorState problem={problem} onRetry={() => query.refetch()} />;
  }

  if (items.length === 0) {
    return (
      <EmptyState
        icon={<Package className="h-10 w-10" />}
        title="Sin artículos."
        description={
          canCrear
            ? 'Crea el primero o ajusta los filtros.'
            : 'Cuando se creen, aparecerán aquí.'
        }
        action={
          canCrear ? (
            <Button size="sm" onClick={onAbrirNuevo}>
              <Plus className="mr-1 h-4 w-4" />
              Nuevo artículo
            </Button>
          ) : undefined
        }
      />
    );
  }

  return <ListaArticulosCompacta items={items} idActivo={idActivo} />;
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center rounded-md border border-dashed bg-muted/20 p-8 text-center">
      <div className="space-y-1 text-muted-foreground">
        <Package className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona un artículo de la lista</p>
        <p className="text-xs">para ver su detalle.</p>
      </div>
    </div>
  );
}
