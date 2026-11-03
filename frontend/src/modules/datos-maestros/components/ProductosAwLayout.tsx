import { CargaMaestrosDialog } from './CargaMaestrosDialog';
import { PageHeader } from '@/components/erp/PageHeader';
import { useMemo, useState, type ReactNode } from 'react';
import { Boxes, PaintBucket, Plus, RefreshCw, TriangleAlert } from 'lucide-react';
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
import { useProductosAwInfinito } from '@/modules/datos-maestros/api';
import { EstatusCatalogo, OrigenMaster } from '@/modules/datos-maestros/api/types';
import type { ListarProductosAwFiltros } from '@/modules/datos-maestros/api/keys';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ListaProductosAwCompacta } from '@/modules/datos-maestros/components/ListaProductosAwCompacta';
import { SheetSincronizacionProductosAw } from '@/modules/datos-maestros/components/SheetSincronizacionProductosAw';
import { useNuevoProductoAw } from '@/modules/datos-maestros/components/nuevo-producto-aw-context';
import { F1AFiscalPanel } from './F1AFiscalPanel';
import { MaestroListaCuerpo } from './MaestroLista';
import { CampoFiltro, ChipFiltro, MaestroFiltros, MaestroShell } from './MaestroShell';

/**
 * Layout master-detail (P3 del patrón cross-módulo) para
 * <c>/admin/datos-maestros/productos-aw</c> (ADR-0048). A la vista: descripción, estado y
 * los chips «Fiscales incompletos» (productos sin claves SAT, que bloquean el timbrado) y
 * «Servicios de recubrimiento»; referencia, origen y tipo en «+ Filtro». La API trae
 * primero los completos y después los incompletos.
 */
export interface ProductosAwLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function ProductosAwLayout({ idActivo, detalle }: ProductosAwLayoutProps) {
  const nuevoProductoAw = useNuevoProductoAw();
  const canGestionar = useHasPermission(PermisosCanonicos.DatosMaestrosProductosAwGestionar);

  const [syncAbierto, setSyncAbierto] = useState(false);
  const [referenciaInput, setReferenciaInput] = useState('');
  const [descripcionInput, setDescripcionInput] = useState('');
  const [origen, setOrigen] = useState<string>('');
  const [estatus, setEstatus] = useState<string>('');
  const [tipo, setTipo] = useState<string>('');
  const [soloFiscalesIncompletos, setSoloFiscalesIncompletos] = useState(false);
  const [soloServicios, setSoloServicios] = useState(false);

  const referenciaDeb = useDebouncedValue(referenciaInput, 300);
  const descripcionDeb = useDebouncedValue(descripcionInput, 300);

  const filtros = useMemo<ListarProductosAwFiltros>(
    () => ({
      referencia: referenciaDeb.trim() || undefined,
      descripcion: descripcionDeb.trim() || undefined,
      origen: origen !== '' ? (Number(origen) as OrigenMaster) : undefined,
      estatus: estatus !== '' ? (Number(estatus) as EstatusCatalogo) : undefined,
      fiscalesIncompletos: soloFiscalesIncompletos ? true : undefined,
      tipo: tipo !== '' ? tipo : undefined,
      esServicioRecubrimiento: soloServicios ? true : undefined,
    }),
    [referenciaDeb, descripcionDeb, origen, estatus, tipo, soloFiscalesIncompletos, soloServicios],
  );

  const { query, items, total } = useProductosAwInfinito(filtros);
  const activos = [referenciaInput, origen, tipo].filter((v) => v !== '').length;

  return (
    <>
      <MaestroShell
        idActivo={idActivo}
        etiquetaLista="Lista de productos A+W"
        etiquetaDetalle="Detalle de producto A+W"
        cabecera={
          <PageHeader
            titulo={<>Productos A+W</>}
            acciones={
              <>
                <CargaMaestrosDialog catalogo="productos-aw-fiscal" />
                <div className="flex items-center gap-1">
                  {canGestionar && (
                    <Button size="sm" variant="outline" onClick={() => setSyncAbierto(true)}>
                      <RefreshCw className="mr-1 h-4 w-4" />
                      Sincronizar con A+W
                    </Button>
                  )}
                  {canGestionar && (
                    <Button size="sm" onClick={() => nuevoProductoAw.abrir()}>
                      <Plus className="mr-1 h-4 w-4" />
                      Nuevo
                    </Button>
                  )}
                </div>
              </>
            }
          />
        }
        filtros={
          <MaestroFiltros
            busqueda={descripcionInput}
            onBusqueda={setDescripcionInput}
            placeholder="Buscar por descripción…"
            ariaLabel="Filtrar por descripción"
            estatus={estatus}
            onEstatus={setEstatus}
            masFiltrosActivos={activos}
            chips={
              <>
                {/* Bandeja de trabajo pre-timbrado: fiscalesIncompletos=true trae solo
                    los productos sin claves SAT. */}
                <ChipFiltro
                  activo={soloFiscalesIncompletos}
                  onChange={setSoloFiscalesIncompletos}
                  icono={<TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />}
                >
                  Fiscales incompletos
                </ChipFiltro>
                <ChipFiltro
                  activo={soloServicios}
                  onChange={setSoloServicios}
                  icono={<PaintBucket className="h-3.5 w-3.5" aria-hidden="true" />}
                >
                  Servicios de recubrimiento
                </ChipFiltro>
              </>
            }
            masFiltros={
              <>
                <CampoFiltro etiqueta="Referencia">
                  <Input
                    value={referenciaInput}
                    onChange={(e) => setReferenciaInput(e.target.value)}
                    className="text-xs"
                  />
                </CampoFiltro>
                <Select
                  value={origen === '' ? 'all' : origen}
                  onValueChange={(v) => setOrigen(v === 'all' ? '' : v)}
                >
                  <SelectTrigger className="text-xs" aria-label="Origen">
                    <SelectValue placeholder="Origen" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">Origen: todos</SelectItem>
                    <SelectItem value={String(OrigenMaster.Aw)}>A+W</SelectItem>
                    <SelectItem value={String(OrigenMaster.Manual)}>Manual</SelectItem>
                  </SelectContent>
                </Select>
                <Select
                  value={tipo === '' ? 'all' : tipo}
                  onValueChange={(v) => setTipo(v === 'all' ? '' : v)}
                >
                  <SelectTrigger className="text-xs" aria-label="Tipo">
                    <SelectValue placeholder="Tipo" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">Todos los tipos</SelectItem>
                    {TIPOS_AW.map((t) => (
                      <SelectItem key={t} value={t}>
                        {t}
                      </SelectItem>
                    ))}
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
            plural="productos"
            vacio={
              <EmptyState
                icon={<Boxes className="h-10 w-10" />}
                title="Sin productos A+W."
                description={
                  canGestionar
                    ? 'Crea el primero o ajusta los filtros. Los pedidos A+W también los auto-provisionan.'
                    : 'Cuando se creen, aparecerán aquí.'
                }
                action={
                  canGestionar ? (
                    <Button size="sm" onClick={() => nuevoProductoAw.abrir()}>
                      <Plus className="mr-1 h-4 w-4" />
                      Nuevo producto
                    </Button>
                  ) : undefined
                }
              />
            }
          >
            <ListaProductosAwCompacta items={items} idActivo={idActivo} />
          </MaestroListaCuerpo>
        }
        detalle={detalle}
        sinSeleccion={
          canGestionar ? (
            <F1AFiscalPanel />
          ) : (
            <p className="p-6 text-sm text-ink-muted">
              Selecciona un registro para consultar su detalle.
            </p>
          )
        }
      />
      {canGestionar && (
        <SheetSincronizacionProductosAw open={syncAbierto} onOpenChange={setSyncAbierto} />
      )}
    </>
  );
}

/** Tipos de A+W que se sincronizan, tal cual vienen del origen. */
const TIPOS_AW = ['Vidrio plano', 'VTE', 'VLA', 'VC'] as const;
