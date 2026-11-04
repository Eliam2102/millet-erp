import { CargaMaestrosDialog } from './CargaMaestrosDialog';
import { PageHeader } from '@/components/erp/PageHeader';
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
import { EmptyState } from '@/components/erp';
import { useArticulosInfinito } from '@/modules/datos-maestros/api';
import { EstatusCatalogo, Naturaleza } from '@/modules/datos-maestros/api/types';
import type { ListarArticulosFiltros } from '@/modules/datos-maestros/api/keys';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ListaArticulosCompacta } from '@/modules/datos-maestros/components/ListaArticulosCompacta';
import { useNuevoArticulo } from '@/modules/datos-maestros/components/nuevo-articulo-context';
import { MaestroListaCuerpo } from './MaestroLista';
import { CampoFiltro, MaestroFiltros, MaestroShell } from './MaestroShell';

/**
 * Layout master-detail (P3 del patrón cross-módulo) para
 * <c>/admin/datos-maestros/articulos</c>. Filtros: clave o descripción y estado a la vista;
 * código, unidad y naturaleza en «+ Filtro». Claves en orden natural (lo ordena la
 * API) y «Cargar más» sobre todo el universo. Artículos no tiene noción de «incompleto».
 */
export interface ArticulosLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function ArticulosLayout({ idActivo, detalle }: ArticulosLayoutProps) {
  const nuevoArticulo = useNuevoArticulo();
  const canAdministrar = useHasPermission(PermisosCanonicos.CompartidoCatalogosAdministrar);

  const [codigoInput, setCodigoInput] = useState('');
  const [busquedaInput, setBusquedaInput] = useState('');
  const [unidadInput, setUnidadInput] = useState('');
  const [naturaleza, setNaturaleza] = useState<string>('');
  const [estatus, setEstatus] = useState<string>('');

  const codigoDeb = useDebouncedValue(codigoInput, 300);
  const busquedaDeb = useDebouncedValue(busquedaInput, 300);
  const unidadDeb = useDebouncedValue(unidadInput, 300);

  const filtros = useMemo<ListarArticulosFiltros>(
    () => ({
      codigo: codigoDeb.trim() || undefined,
      q: busquedaDeb.trim() || undefined,
      unidadMedidaDefault: unidadDeb.trim() || undefined,
      naturaleza: naturaleza !== '' ? (Number(naturaleza) as Naturaleza) : undefined,
      estatus: estatus !== '' ? (Number(estatus) as EstatusCatalogo) : undefined,
    }),
    [codigoDeb, busquedaDeb, unidadDeb, naturaleza, estatus],
  );

  const { query, items, total } = useArticulosInfinito(filtros);
  const activos = [codigoInput, unidadInput, naturaleza].filter((v) => v !== '').length;

  return (
    <MaestroShell
      idActivo={idActivo}
      etiquetaLista="Lista de artículos"
      etiquetaDetalle="Detalle de artículo"
      cabecera={
        <PageHeader
          titulo={<>Artículos</>}
          acciones={
            <>
              <CargaMaestrosDialog catalogo="articulos" />
              {canAdministrar && (
                <Button size="sm" onClick={() => nuevoArticulo.abrir()}>
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
          placeholder="Clave o descripción…"
          ariaLabel="Buscar por clave o descripción"
          estatus={estatus}
          onEstatus={setEstatus}
          masFiltrosActivos={activos}
          masFiltros={
            <>
              <CampoFiltro etiqueta="Código">
                <Input
                  value={codigoInput}
                  onChange={(e) => setCodigoInput(e.target.value)}
                  className="text-xs"
                />
              </CampoFiltro>
              <CampoFiltro etiqueta="Unidad de medida (PZA, KG, …)">
                <Input
                  value={unidadInput}
                  onChange={(e) => setUnidadInput(e.target.value)}
                  className="text-xs"
                />
              </CampoFiltro>
              <Select
                value={naturaleza === '' ? 'all' : naturaleza}
                onValueChange={(v) => setNaturaleza(v === 'all' ? '' : v)}
              >
                <SelectTrigger className="text-xs" aria-label="Naturaleza">
                  <SelectValue placeholder="Naturaleza" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">Naturaleza: todas</SelectItem>
                  <SelectItem value={String(Naturaleza.Estandar)}>Estándar</SelectItem>
                  <SelectItem value={String(Naturaleza.Servicio)}>Servicio</SelectItem>
                  <SelectItem value={String(Naturaleza.Critico)}>Crítico</SelectItem>
                  <SelectItem value={String(Naturaleza.Riesgo)}>Riesgo</SelectItem>
                </SelectContent>
              </Select>
            </>
          }
        />
      }
      lista={
        <MaestroListaCuerpo
          query={query}
          cargados={items.length}
          total={total}
          plural="artículos"
          vacio={
            <EmptyState
              icon={<Package className="h-10 w-10" />}
              title="Sin artículos."
              description={
                canAdministrar
                  ? 'Crea el primero o ajusta los filtros.'
                  : 'Cuando se creen, aparecerán aquí.'
              }
              action={
                canAdministrar ? (
                  <Button size="sm" onClick={() => nuevoArticulo.abrir()}>
                    <Plus className="mr-1 h-4 w-4" />
                    Nuevo artículo
                  </Button>
                ) : undefined
              }
            />
          }
        >
          <ListaArticulosCompacta items={items} idActivo={idActivo} />
        </MaestroListaCuerpo>
      }
      detalle={detalle}
      sinSeleccion={<PlaceholderSinSeleccion />}
      mostrarSinSeleccionEnMovil={false}
    />
  );
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center rounded-md border border-dashed bg-surface-muted/20 p-8 text-center">
      <div className="space-y-1 text-ink-muted">
        <Package className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona un artículo de la lista</p>
        <p className="text-xs">para ver su detalle.</p>
      </div>
    </div>
  );
}
