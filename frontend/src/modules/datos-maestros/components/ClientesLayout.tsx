import { CargaMaestrosDialog } from './CargaMaestrosDialog';
import { PageHeader } from '@/components/erp/PageHeader';
import { useMemo, useState, type ReactNode } from 'react';
import { Plus, RefreshCw, TriangleAlert, Users } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { UppercaseInput } from '@/components/erp/forms/UppercaseInput';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState } from '@/components/erp';
import { useClientesInfinito } from '@/modules/datos-maestros/api';
import { EstatusCatalogo, OrigenMaster } from '@/modules/datos-maestros/api/types';
import type { ListarClientesFiltros } from '@/modules/datos-maestros/api/keys';
import type { ResultadoSincronizacion } from '@/modules/datos-maestros/api/types';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ListaClientesCompacta } from '@/modules/datos-maestros/components/ListaClientesCompacta';
import { useNuevoCliente } from '@/modules/datos-maestros/components/nuevo-cliente-context';
import { SheetSincronizacionClientes } from '@/modules/datos-maestros/components/SheetSincronizacionClientes';
import { F1AClientesPanel } from './F1AClientesPanel';
import { useClientesFiltrosStore } from '@/modules/datos-maestros/stores/clientes-filtros-store';
import { MaestroListaCuerpo } from './MaestroLista';
import { CampoFiltro, ChipFiltro, MaestroFiltros, MaestroShell } from './MaestroShell';

/**
 * Layout master-detail (P3 del patrón cross-módulo) para
 * <c>/admin/datos-maestros/clientes</c> (ADR-0048). A la vista: razón social, estado y los
 * chips «Fiscales incompletos» (bandeja pre-timbrado: sin RFC/régimen/CP) y «Clientes sin
 * clasificar»; RFC, origen y sincronización en «+ Filtro». La API trae primero los
 * completos y después los incompletos, con la clave en orden natural.
 *
 * <para>El permiso de mutación es el granular
 * <c>datos_maestros.clientes.gestionar</c> (el backend lo exige en los
 * endpoints de escritura; NO usa el grueso
 * <c>compartido.catalogos.administrar</c>).</para>
 */
export interface ClientesLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function ClientesLayout({ idActivo, detalle }: ClientesLayoutProps) {
  const nuevoCliente = useNuevoCliente();
  const canGestionar = useHasPermission(PermisosCanonicos.DatosMaestrosClientesGestionar);

  const canSincronizar = useHasPermission(PermisosCanonicos.DatosMaestrosClientesSincronizar);
  const [syncAbierto, setSyncAbierto] = useState(false);

  const {
    sinClasificar,
    setSinClasificar,
    rfcInput,
    setRfcInput,
    razonInput,
    setRazonInput,
    origen,
    setOrigen,
    estatus,
    setEstatus,
    resultadoSync,
    setResultadoSync,
    soloFiscalesIncompletos,
    setSoloFiscalesIncompletos,
  } = useClientesFiltrosStore();

  const rfcDebounced = useDebouncedValue(rfcInput, 300);
  const razonDebounced = useDebouncedValue(razonInput, 300);

  const filtros = useMemo<ListarClientesFiltros>(
    () => ({
      rfc: rfcDebounced.trim() || undefined,
      razonSocial: razonDebounced.trim() || undefined,
      origen: origen !== '' ? (Number(origen) as OrigenMaster) : undefined,
      estatus: estatus !== '' ? (Number(estatus) as EstatusCatalogo) : undefined,
      sinClasificar: sinClasificar || undefined,
      fiscalesIncompletos: soloFiscalesIncompletos ? true : undefined,
      resultadoSincronizacion:
        resultadoSync !== '' ? (resultadoSync as ResultadoSincronizacion) : undefined,
    }),
    [
      sinClasificar,
      rfcDebounced,
      razonDebounced,
      origen,
      estatus,
      soloFiscalesIncompletos,
      resultadoSync,
    ],
  );

  const { query, items, total } = useClientesInfinito(filtros);
  const activos = [rfcInput, origen, resultadoSync].filter((v) => v !== '').length;

  return (
    <>
      <MaestroShell
        idActivo={idActivo}
        etiquetaLista="Lista de clientes"
        etiquetaDetalle="Detalle de cliente"
        mostrarSinSeleccionEnMovil={canGestionar}
        cabecera={
          <PageHeader
            titulo={<>Clientes</>}
            acciones={
              <>
                <CargaMaestrosDialog catalogo="clientes" />
                <div className="flex items-center gap-1">
                  {canSincronizar && (
                    <Button size="sm" variant="outline" onClick={() => setSyncAbierto(true)}>
                      <RefreshCw className="mr-1 h-4 w-4" />
                      Sincronizar
                    </Button>
                  )}
                  {canGestionar && (
                    <Button size="sm" onClick={() => nuevoCliente.abrir()}>
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
            busqueda={razonInput}
            onBusqueda={setRazonInput}
            placeholder="Buscar por razón social…"
            ariaLabel="Filtrar por razón social"
            estatus={estatus}
            onEstatus={setEstatus}
            masFiltrosActivos={activos}
            chips={
              <>
                {/* Bandeja de trabajo pre-timbrado: fiscalesIncompletos=true trae
                    solo los clientes que aún no pueden timbrar. */}
                <ChipFiltro
                  activo={soloFiscalesIncompletos}
                  onChange={setSoloFiscalesIncompletos}
                  icono={<TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />}
                >
                  Fiscales incompletos
                </ChipFiltro>
                <ChipFiltro activo={sinClasificar} onChange={setSinClasificar}>
                  Clientes sin clasificar
                </ChipFiltro>
              </>
            }
            masFiltros={
              <>
                <CampoFiltro etiqueta="RFC">
                  <UppercaseInput
                    value={rfcInput}
                    onChange={(e) => setRfcInput(e.target.value)}
                    className="font-mono text-xs"
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
                {/* Bandeja de revisión de la sincronización A+W (F1-ADM-06): el
                    backend filtra por un solo resultado a la vez. */}
                <Select
                  value={resultadoSync === '' ? 'all' : resultadoSync}
                  onValueChange={(v) => setResultadoSync(v === 'all' ? '' : v)}
                >
                  <SelectTrigger className="text-xs" aria-label="Resultado de sincronización">
                    <SelectValue placeholder="Resultado de sincronización" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">Sincronización: todos</SelectItem>
                    <SelectItem value="Pendiente">Pendiente de validación</SelectItem>
                    <SelectItem value="Conflicto">Conflicto</SelectItem>
                    <SelectItem value="Error">Error</SelectItem>
                    <SelectItem value="Aplicado">Aplicado</SelectItem>
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
            plural="clientes"
            vacio={
              <EmptyState
                icon={<Users className="h-10 w-10" />}
                title="Sin clientes."
                description={
                  canGestionar
                    ? 'Crea el primero o ajusta los filtros. Los pedidos A+W también los auto-provisionan.'
                    : 'Cuando se creen, aparecerán aquí.'
                }
                action={
                  canGestionar ? (
                    <Button size="sm" onClick={() => nuevoCliente.abrir()}>
                      <Plus className="mr-1 h-4 w-4" />
                      Nuevo cliente
                    </Button>
                  ) : undefined
                }
              />
            }
          >
            <ListaClientesCompacta items={items} idActivo={idActivo} />
          </MaestroListaCuerpo>
        }
        detalle={detalle}
        sinSeleccion={
          canGestionar ? (
            <F1AClientesPanel />
          ) : (
            <p className="p-6 text-sm text-ink-muted">
              Selecciona un registro para consultar su detalle.
            </p>
          )
        }
      />
      {canSincronizar && (
        <SheetSincronizacionClientes open={syncAbierto} onOpenChange={setSyncAbierto} />
      )}
    </>
  );
}
