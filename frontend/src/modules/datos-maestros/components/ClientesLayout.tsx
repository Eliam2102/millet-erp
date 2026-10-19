import { useMemo, useState, type ReactNode } from 'react';
import { Plus, RefreshCw, TriangleAlert, Users } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { UppercaseInput } from '@/components/erp/forms/UppercaseInput';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { useClientes } from '@/modules/datos-maestros/api';
import {
  EstatusCatalogo,
  OrigenMaster,
} from '@/modules/datos-maestros/api/types';
import type { ListarClientesFiltros } from '@/modules/datos-maestros/api/keys';
import type { ResultadoSincronizacion } from '@/modules/datos-maestros/api/types';
import { esApiError } from '@/lib/api';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ListaClientesCompacta } from '@/modules/datos-maestros/components/ListaClientesCompacta';
import { useNuevoCliente } from '@/modules/datos-maestros/components/nuevo-cliente-context';
import { SheetSincronizacionClientes } from '@/modules/datos-maestros/components/SheetSincronizacionClientes';
import { F1AClientesPanel } from './F1AClientesPanel';
import { useClientesFiltrosStore } from '@/modules/datos-maestros/stores/clientes-filtros-store';
import { cn } from '@/lib/utils';

/**
 * Layout master-detail (P3 del patrón cross-módulo) para
 * <c>/admin/datos-maestros/clientes</c> (ADR-0048). Análogo a
 * <see cref="ProveedoresLayout"/>; filtros: RFC, razón social, origen
 * (A+W/Manual), estatus y el toggle "Fiscales incompletos" — la bandeja
 * de trabajo pre-timbrado (clientes auto-provisionados sin
 * RFC/régimen/CP).
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
  const canGestionar = useHasPermission(
    PermisosCanonicos.DatosMaestrosClientesGestionar,
  );

  const canSincronizar = useHasPermission(
    PermisosCanonicos.DatosMaestrosClientesSincronizar,
  );
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
      estatus:
        estatus !== '' ? (Number(estatus) as EstatusCatalogo) : undefined,
      sinClasificar: sinClasificar || undefined,
      fiscalesIncompletos: soloFiscalesIncompletos ? true : undefined,
      resultadoSincronizacion:
        resultadoSync !== ''
          ? (resultadoSync as ResultadoSincronizacion)
          : undefined,
      limit: 200,
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

  const clientesQuery = useClientes(filtros);
  const items = useMemo(
    () => clientesQuery.data?.items ?? [],
    [clientesQuery.data],
  );

  return (
    <div className="flex flex-col gap-4 md:h-[calc(100vh-3.5rem-3rem)] md:flex-row">
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de clientes"
        data-print="hidden"
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h1 className="text-xl font-semibold tracking-tight">Clientes</h1>
          <div className="flex items-center gap-1">
            {canSincronizar && (
              <Button
                size="sm"
                variant="outline"
                onClick={() => setSyncAbierto(true)}
              >
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
        </div>

        <Button variant={sinClasificar ? "default" : "secondary"} size="sm" aria-pressed={sinClasificar} onClick={() => setSinClasificar(!sinClasificar)}>Clientes sin clasificar</Button>
        <FiltrosBloque
          rfc={rfcInput}
          onRfc={setRfcInput}
          razon={razonInput}
          onRazon={setRazonInput}
          origen={origen}
          onOrigen={setOrigen}
          estatus={estatus}
          onEstatus={setEstatus}
          resultadoSync={resultadoSync}
          onResultadoSync={setResultadoSync}
          soloFiscalesIncompletos={soloFiscalesIncompletos}
          onSoloFiscalesIncompletos={setSoloFiscalesIncompletos}
        />

        {clientesQuery.data && clientesQuery.data.total > items.length && (
          <p role="status" className="text-xs text-ink-muted">
            Mostrando {items.length} de {clientesQuery.data.total} clientes. Ajusta los filtros para acotar la lista.
          </p>
        )}

        <div className="flex-1 overflow-y-auto rounded-md border bg-card">
          <RenderLista
            query={clientesQuery}
            items={items}
            idActivo={idActivo}
            canCrear={canGestionar}
            onAbrirNuevo={() => nuevoCliente.abrir()}
          />
        </div>
      </aside>

      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null && !canGestionar ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de cliente"
      >
        {idActivo == null ? canGestionar ? <F1AClientesPanel /> : <p className="p-6 text-sm text-ink-muted">Selecciona un registro para consultar su detalle.</p> : detalle}
      </section>

      {canSincronizar && (
        <SheetSincronizacionClientes
          open={syncAbierto}
          onOpenChange={setSyncAbierto}
        />
      )}
    </div>
  );
}

interface FiltrosBloqueProps {
  rfc: string;
  onRfc: (v: string) => void;
  razon: string;
  onRazon: (v: string) => void;
  origen: string;
  onOrigen: (v: string) => void;
  estatus: string;
  onEstatus: (v: string) => void;
  resultadoSync: string;
  onResultadoSync: (v: string) => void;
  soloFiscalesIncompletos: boolean;
  onSoloFiscalesIncompletos: (v: boolean) => void;
}

function FiltrosBloque({
  rfc,
  onRfc,
  razon,
  onRazon,
  origen,
  onOrigen,
  estatus,
  onEstatus,
  resultadoSync,
  onResultadoSync,
  soloFiscalesIncompletos,
  onSoloFiscalesIncompletos,
}: FiltrosBloqueProps) {
  return (
    <div className="space-y-2 rounded-md border bg-muted/20 p-2">
      <UppercaseInput
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
          value={origen === '' ? 'all' : origen}
          onValueChange={(v) => onOrigen(v === 'all' ? '' : v)}
        >
          <SelectTrigger className="h-8 text-xs" aria-label="Origen">
            <SelectValue placeholder="Origen" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Todos</SelectItem>
            <SelectItem value={String(OrigenMaster.Aw)}>A+W</SelectItem>
            <SelectItem value={String(OrigenMaster.Manual)}>Manual</SelectItem>
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
      {/* Bandeja de revisión de la sincronización A+W (F1-ADM-06): el
          backend filtra por un solo resultado a la vez. */}
      <Select
        value={resultadoSync === '' ? 'all' : resultadoSync}
        onValueChange={(v) => onResultadoSync(v === 'all' ? '' : v)}
      >
        <SelectTrigger
          className="h-8 text-xs"
          aria-label="Resultado de sincronización"
        >
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
      {/* Bandeja de trabajo pre-timbrado: fiscalesIncompletos=true trae
          solo los clientes que aún no pueden timbrar. */}
      <Button
        type="button"
        variant="outline"
        size="sm"
        aria-pressed={soloFiscalesIncompletos}
        onClick={() => onSoloFiscalesIncompletos(!soloFiscalesIncompletos)}
        className={cn(
          'h-8 w-full justify-start text-xs font-normal',
          soloFiscalesIncompletos &&
            'border-amber-300 bg-amber-500/10 text-amber-700 hover:bg-amber-500/15 hover:text-amber-700 dark:text-amber-300 dark:hover:text-amber-300',
        )}
      >
        <TriangleAlert className="mr-1.5 h-3.5 w-3.5" />
        Fiscales incompletos
      </Button>
    </div>
  );
}

interface RenderListaProps {
  query: ReturnType<typeof useClientes>;
  items: ReturnType<typeof useClientes>['data'] extends
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
        icon={<Users className="h-10 w-10" />}
        title="Sin clientes."
        description={
          canCrear
            ? 'Crea el primero o ajusta los filtros. Los pedidos A+W también los auto-provisionan.'
            : 'Cuando se creen, aparecerán aquí.'
        }
        action={
          canCrear ? (
            <Button size="sm" onClick={onAbrirNuevo}>
              <Plus className="mr-1 h-4 w-4" />
              Nuevo cliente
            </Button>
          ) : undefined
        }
      />
    );
  }

  return <ListaClientesCompacta items={items} idActivo={idActivo} />;
}
