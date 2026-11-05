import { CargaMaestrosDialog } from './CargaMaestrosDialog';
import { PageHeader } from '@/components/erp/PageHeader';
import { useMemo, useState, type ReactNode } from 'react';
import { Ban, Plus, TriangleAlert, Truck } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState } from '@/components/erp';
import { useProveedoresInfinito } from '@/modules/datos-maestros/api';
import { EstatusCatalogo, TipoPersonaProveedor } from '@/modules/datos-maestros/api/types';
import type { ListarProveedoresFiltros } from '@/modules/datos-maestros/api/keys';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ListaProveedoresCompacta } from '@/modules/datos-maestros/components/ListaProveedoresCompacta';
import { useNuevoProveedor } from '@/modules/datos-maestros/components/nuevo-proveedor-context';
import { MaestroListaCuerpo } from './MaestroLista';
import { ChipFiltro, MaestroFiltros, MaestroShell } from './MaestroShell';

/**
 * Layout master-detail (P3 del patrón cross-módulo) para
 * <c>/admin/datos-maestros/proveedores</c>. A la vista: búsqueda (clave, RFC, nombre o
 * razón social), estado y los chips «Expediente incompleto» y «Proveedores bloqueados»;
 * tipo de persona en «+ Filtro». La API trae primero los de expediente completo y después
 * los incompletos, con la clave en orden natural. Filtros con debounce 300 ms.
 */
export interface ProveedoresLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function ProveedoresLayout({ idActivo, detalle }: ProveedoresLayoutProps) {
  const nuevoProveedor = useNuevoProveedor();
  const canAdministrar = useHasPermission(PermisosCanonicos.CompartidoCatalogosAdministrar);

  // Inputs locales del bloque de filtros — se debouncean antes de
  // alimentar el hook para evitar un fetch por keystroke.
  const [busquedaInput, setBusquedaInput] = useState('');
  const [tipoPersona, setTipoPersona] = useState<string>('');
  const [estatus, setEstatus] = useState<string>('');
  const [soloIncompleto, setSoloIncompleto] = useState(false);
  const [soloBloqueados, setSoloBloqueados] = useState(false);

  const busquedaDebounced = useDebouncedValue(busquedaInput, 300);

  const filtros = useMemo<ListarProveedoresFiltros>(
    () => ({
      q: busquedaDebounced.trim() || undefined,
      tipoPersona: tipoPersona !== '' ? (Number(tipoPersona) as TipoPersonaProveedor) : undefined,
      estatus: estatus !== '' ? (Number(estatus) as EstatusCatalogo) : undefined,
      soloExpedienteIncompleto: soloIncompleto || undefined,
      bloqueado: soloBloqueados || undefined,
    }),
    [busquedaDebounced, tipoPersona, estatus, soloIncompleto, soloBloqueados],
  );

  const { query, items, total } = useProveedoresInfinito(filtros);

  return (
    <MaestroShell
      idActivo={idActivo}
      etiquetaLista="Lista de proveedores"
      etiquetaDetalle="Detalle de proveedor"
      cabecera={
        <PageHeader
          titulo={<>Proveedores</>}
          acciones={
            <>
              <CargaMaestrosDialog catalogo="proveedores" />
              {canAdministrar && (
                <Button size="sm" onClick={() => nuevoProveedor.abrir()}>
                  <Plus className="mr-1 h-4 w-4" />
                  Nuevo
                </Button>
              )}
            </>
          }
        />
      }
      filtros={
        <MaestroFiltros
          busqueda={busquedaInput}
          onBusqueda={setBusquedaInput}
          placeholder="Clave, RFC, Tax ID, nombre o razón social…"
          ariaLabel="Buscar proveedor"
          estatus={estatus}
          onEstatus={setEstatus}
          masFiltrosActivos={tipoPersona !== '' ? 1 : 0}
          chips={
            <>
              <ChipFiltro
                activo={soloIncompleto}
                onChange={setSoloIncompleto}
                icono={<TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />}
              >
                Expediente incompleto
              </ChipFiltro>
              <ChipFiltro
                activo={soloBloqueados}
                onChange={setSoloBloqueados}
                icono={<Ban className="h-3.5 w-3.5" aria-hidden="true" />}
              >
                Bloqueados
              </ChipFiltro>
            </>
          }
          masFiltros={
            <Select
              value={tipoPersona === '' ? 'all' : tipoPersona}
              onValueChange={(v) => setTipoPersona(v === 'all' ? '' : v)}
            >
              <SelectTrigger className="text-xs" aria-label="Tipo persona">
                <SelectValue placeholder="Tipo" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">Tipo de persona: todos</SelectItem>
                <SelectItem value={String(TipoPersonaProveedor.Moral)}>Moral</SelectItem>
                <SelectItem value={String(TipoPersonaProveedor.Fisica)}>Física</SelectItem>
              </SelectContent>
            </Select>
          }
        />
      }
      lista={
        <MaestroListaCuerpo
          query={query}
          cargados={items.length}
          total={total}
          plural="proveedores"
          vacio={
            <EmptyState
              icon={<Truck className="h-10 w-10" />}
              title="Sin proveedores."
              description={
                canAdministrar
                  ? 'Crea el primero o ajusta los filtros.'
                  : 'Cuando se creen, aparecerán aquí.'
              }
              action={
                canAdministrar ? (
                  <Button size="sm" onClick={() => nuevoProveedor.abrir()}>
                    <Plus className="mr-1 h-4 w-4" />
                    Nuevo proveedor
                  </Button>
                ) : undefined
              }
            />
          }
        >
          <ListaProveedoresCompacta items={items} idActivo={idActivo} />
        </MaestroListaCuerpo>
      }
      detalle={detalle}
      sinSeleccion={<PlaceholderSinSeleccion />}
    />
  );
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center rounded-md border border-dashed bg-surface-muted/20 p-8 text-center">
      <div className="space-y-1 text-ink-muted">
        <Truck className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona un proveedor de la lista</p>
        <p className="text-xs">para ver su detalle.</p>
      </div>
    </div>
  );
}
