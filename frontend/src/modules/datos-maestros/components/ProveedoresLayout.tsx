import { useMemo, useState, type ReactNode } from 'react';
import { Plus, Truck } from 'lucide-react';
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
import { useProveedores } from '@/modules/datos-maestros/api';
import {
  EstatusCatalogo,
  TipoPersonaProveedor,
} from '@/modules/datos-maestros/api/types';
import type { ListarProveedoresFiltros } from '@/modules/datos-maestros/api/keys';
import { esApiError } from '@/lib/api';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ListaProveedoresCompacta } from '@/modules/datos-maestros/components/ListaProveedoresCompacta';
import { useNuevoProveedor } from '@/modules/datos-maestros/components/nuevo-proveedor-context';
import { cn } from '@/lib/utils';

/**
 * Layout master-detail (P3 del patrón cross-módulo) para
 * <c>/admin/datos-maestros/proveedores</c>.
 *
 * <para><b>Master</b> (320px sticky a la izquierda) — H1 + botón
 * "Nuevo proveedor" (si <c>compartido.catalogos.administrar</c>) +
 * bloque de filtros server-side (RFC, Razón social, Tipo persona,
 * Estatus) + lista compacta. Filtros con debounce 300 ms y query key
 * que los incluye, así un cambio invalida la query y refetchea.</para>
 *
 * <para><b>Panel detalle</b> recibe via <c>detalle</c> el contenido
 * para el id seleccionado, o un placeholder cuando <c>idActivo</c>
 * es <c>null</c>. Mobile sub-md aplica drill-down.</para>
 */
export interface ProveedoresLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function ProveedoresLayout({
  idActivo,
  detalle,
}: ProveedoresLayoutProps) {
  const nuevoProveedor = useNuevoProveedor();
  const canAdministrar = useHasPermission(
    PermisosCanonicos.CompartidoCatalogosAdministrar,
  );

  // Inputs locales del bloque de filtros — se debouncean antes de
  // alimentar el hook para evitar un fetch por keystroke.
  const [rfcInput, setRfcInput] = useState('');
  const [razonInput, setRazonInput] = useState('');
  const [tipoPersona, setTipoPersona] = useState<string>('');
  const [estatus, setEstatus] = useState<string>('');

  const rfcDebounced = useDebouncedValue(rfcInput, 300);
  const razonDebounced = useDebouncedValue(razonInput, 300);

  const filtros = useMemo<ListarProveedoresFiltros>(
    () => ({
      rfc: rfcDebounced.trim() || undefined,
      razonSocial: razonDebounced.trim() || undefined,
      tipoPersona:
        tipoPersona !== ''
          ? (Number(tipoPersona) as TipoPersonaProveedor)
          : undefined,
      estatus:
        estatus !== '' ? (Number(estatus) as EstatusCatalogo) : undefined,
      limit: 200,
    }),
    [rfcDebounced, razonDebounced, tipoPersona, estatus],
  );

  const proveedoresQuery = useProveedores(filtros);
  const items = useMemo(
    () => proveedoresQuery.data?.items ?? [],
    [proveedoresQuery.data],
  );

  return (
    <div className="flex flex-col gap-4 md:h-[calc(100vh-3.5rem-3rem)] md:flex-row">
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de proveedores"
        data-print="hidden"
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h1 className="text-xl font-semibold tracking-tight">Proveedores</h1>
          {canAdministrar && (
            <Button size="sm" onClick={() => nuevoProveedor.abrir()}>
              <Plus className="mr-1 h-4 w-4" />
              Nuevo
            </Button>
          )}
        </div>

        <FiltrosBloque
          rfc={rfcInput}
          onRfc={setRfcInput}
          razon={razonInput}
          onRazon={setRazonInput}
          tipoPersona={tipoPersona}
          onTipoPersona={setTipoPersona}
          estatus={estatus}
          onEstatus={setEstatus}
        />

        <div className="flex-1 overflow-y-auto rounded-md border bg-card">
          <RenderLista
            query={proveedoresQuery}
            items={items}
            idActivo={idActivo}
            canCrear={canAdministrar}
            onAbrirNuevo={() => nuevoProveedor.abrir()}
          />
        </div>
      </aside>

      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de proveedor"
      >
        {idActivo == null ? <PlaceholderSinSeleccion /> : detalle}
      </section>
    </div>
  );
}

interface FiltrosBloqueProps {
  rfc: string;
  onRfc: (v: string) => void;
  razon: string;
  onRazon: (v: string) => void;
  tipoPersona: string;
  onTipoPersona: (v: string) => void;
  estatus: string;
  onEstatus: (v: string) => void;
}

function FiltrosBloque({
  rfc,
  onRfc,
  razon,
  onRazon,
  tipoPersona,
  onTipoPersona,
  estatus,
  onEstatus,
}: FiltrosBloqueProps) {
  return (
    <div className="space-y-2 rounded-md border bg-muted/20 p-2">
      <Input
        placeholder="RFC…"
        value={rfc}
        onChange={(e) => onRfc(e.target.value)}
        className="h-8 text-xs"
        aria-label="Filtrar por RFC"
      />
      <Input
        placeholder="Razón social…"
        value={razon}
        onChange={(e) => onRazon(e.target.value)}
        className="h-8 text-xs"
        aria-label="Filtrar por razón social"
      />
      <div className="flex gap-2">
        <Select
          value={tipoPersona === '' ? 'all' : tipoPersona}
          onValueChange={(v) => onTipoPersona(v === 'all' ? '' : v)}
        >
          <SelectTrigger className="h-8 text-xs" aria-label="Tipo persona">
            <SelectValue placeholder="Tipo" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Todos</SelectItem>
            <SelectItem value={String(TipoPersonaProveedor.Moral)}>
              Moral
            </SelectItem>
            <SelectItem value={String(TipoPersonaProveedor.Fisica)}>
              Física
            </SelectItem>
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
  query: ReturnType<typeof useProveedores>;
  items: ReturnType<typeof useProveedores>['data'] extends
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
        icon={<Truck className="h-10 w-10" />}
        title="Sin proveedores."
        description={
          canCrear
            ? 'Crea el primero o ajusta los filtros.'
            : 'Cuando se creen, aparecerán aquí.'
        }
        action={
          canCrear ? (
            <Button size="sm" onClick={onAbrirNuevo}>
              <Plus className="mr-1 h-4 w-4" />
              Nuevo proveedor
            </Button>
          ) : undefined
        }
      />
    );
  }

  return <ListaProveedoresCompacta items={items} idActivo={idActivo} />;
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center rounded-md border border-dashed bg-muted/20 p-8 text-center">
      <div className="space-y-1 text-muted-foreground">
        <Truck className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona un proveedor de la lista</p>
        <p className="text-xs">para ver su detalle.</p>
      </div>
    </div>
  );
}
