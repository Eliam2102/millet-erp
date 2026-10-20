import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { BarraFiltros, PaginacionTabla } from '@/components/erp';
import { TabsContabilidad } from '../components/TabsContabilidad';
import { ESTADOS_PLANTILLA } from '../lib/estados-ui';
import { PageHeader } from '@/components/erp/PageHeader';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Play, Plus, Repeat } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useFormIdempotencyKey } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useGenerarPendientes, usePlantillasRecurrentes } from '../api/recurrentes';
import type {
  EstadoPlantillaRecurrente,
  PlantillaRecurrente,
  ResultadoGeneracion as Resultado,
} from '../api/recurrentes-types';
import { EstadoPlantillaBadge } from '../components/recurrentes/EstadoPlantillaBadge';
import { PlantillaSheet } from '../components/recurrentes/PlantillaSheet';
import { ResultadoGeneracion } from '../components/recurrentes/ResultadoGeneracion';
import { monto } from '../lib/polizas';
import { etiquetaMes, mensajeRecurrente, type RecurrentesSearch } from '../lib/recurrentes';

const VISTAS = Object.entries(ESTADOS_PLANTILLA).map(([estado, ui]) => ({
  estado: estado as EstadoPlantillaRecurrente,
  label: ui.texto,
}));

interface Props {
  search: RecurrentesSearch;
  onSearch: (next: RecurrentesSearch) => void;
  /** Abre el detalle de una plantilla (tras guardar una nueva). */
  onAbrir: (id: string) => void;
}

/**
 * Bandeja de plantillas de pólizas recurrentes (P1, K10.4): vistas por estado, «Nueva plantilla» y «Generar pendientes»
 * (corre ahora lo que el proceso de fondo genera cada hora).
 */
export function PolizasRecurrentesPage({ search, onSearch, onAbrir }: Props) {
  const puedeAdministrar = useHasPermission(PermisosCanonicos.ContabilidadRecurrentesAdministrar);
  const [nueva, setNueva] = useState(false);
  const [resultado, setResultado] = useState<Resultado | null>(null);
  const [mensaje, setMensaje] = useState('');
  const plantillas = usePlantillasRecurrentes(search.estado);
  const generar = useGenerarPendientes();
  const keyGenerar = useFormIdempotencyKey();
  const items = plantillas.data ?? [];
  const pagina = usePaginacionLocal(items, search.estado ?? '');

  function onGenerar() {
    setMensaje('');
    setResultado(null);
    generar.mutate(
      { idempotencyKey: keyGenerar },
      { onSuccess: setResultado, onError: (e) => setMensaje(mensajeRecurrente(e)) },
    );
  }

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <Repeat
              className="mr-2 inline-block size-5 text-brand"
              strokeWidth={1.6}
              aria-hidden="true"
            />
            Pólizas recurrentes
          </>
        }
        descripcion={
          <>
            Plantillas que generan la póliza de cada mes en su día, con reversa automática opcional
            el día 1 del mes siguiente.
          </>
        }
        acciones={
          <>
            {puedeAdministrar && (
              <div className="ml-auto flex flex-wrap gap-2" data-print="hidden">
                <Button variant="secondary" onClick={onGenerar} disabled={generar.isPending}>
                  <Play className="mr-1 size-3.5" aria-hidden="true" />
                  {generar.isPending ? 'Generando…' : 'Generar pendientes'}
                </Button>
                <Button onClick={() => setNueva(true)}>
                  <Plus className="mr-1 size-3.5" aria-hidden="true" />
                  Nueva plantilla
                </Button>
              </div>
            )}
          </>
        }
      />

      {mensaje && (
        <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">
          {mensaje}
        </p>
      )}
      {resultado && (
        <ResultadoGeneracion resultado={resultado} onCerrar={() => setResultado(null)} />
      )}

      <ListadoModulo
        tabs={
          <TabsContabilidad
            estados={VISTAS}
            valor={search.estado}
            onChange={(estado) => onSearch({ ...search, estado })}
          />
        }
        filtros={<BarraFiltros total={`${items.length} plantillas`} />}
        pie={
          <PaginacionTabla
            total={items.length}
            offset={pagina.offset}
            limit={10}
            tamanos={[]}
            onChange={pagina.onChange}
          />
        }
      >
        {plantillas.isLoading ? (
          <div className="flex flex-col gap-2 p-4" aria-label="Cargando plantillas">
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
          </div>
        ) : plantillas.isError ? (
          <div role="alert" className="flex items-center gap-2 p-4 text-sm text-danger-fg">
            No se pudieron cargar las plantillas. {mensajeRecurrente(plantillas.error)}
            <Button variant="ghost" size="sm" onClick={() => void plantillas.refetch()}>
              Reintentar
            </Button>
          </div>
        ) : items.length === 0 ? (
          <p className="p-6 text-sm text-ink-muted">
            {search.estado
              ? 'Ninguna plantilla en este estado.'
              : 'Aún no hay plantillas. Prepara la primera con «Nueva plantilla».'}
          </p>
        ) : (
          <div className="overflow-x-auto">
            <Table className="w-full min-w-245 text-sm">
              <TableHeader className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
                <TableRow>
                  <TableHead className="h-9 px-3">Plantilla</TableHead>
                  <TableHead className="px-3">Tipo</TableHead>
                  <TableHead className="px-3">Inicio</TableHead>
                  <TableHead className="px-3 text-right">Generadas</TableHead>
                  <TableHead className="px-3 text-right">Importe</TableHead>
                  <TableHead className="px-3">Reversa</TableHead>
                  <TableHead className="px-3">Nacen</TableHead>
                  <TableHead className="px-3">Estado</TableHead>
                  <TableHead className="px-3">Preparó</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {pagina.visibles.map((p) => (
                  <TableRow key={p.id} className="h-12 border-b border-line-row">
                    <TableCell className="max-w-70 truncate px-3">
                      <Link
                        to="/contabilidad/polizas/recurrentes/$id"
                        params={{ id: p.id }}
                        search={search}
                        className="font-medium text-brand hover:underline"
                      >
                        {p.nombre}
                      </Link>
                    </TableCell>
                    <TableCell className="px-3 font-mono text-xs">{p.tipoClave}</TableCell>
                    <TableCell className="whitespace-nowrap px-3 text-ink-secondary">
                      {etiquetaMes(p.anioInicio, p.mesInicio)}
                    </TableCell>
                    <TableCell className="whitespace-nowrap px-3 text-right tabular-nums">
                      {p.generadas} de {p.numeroPeriodos}
                    </TableCell>
                    <TableCell className="whitespace-nowrap px-3 text-right font-medium tabular-nums">
                      {monto(p.importe)}
                    </TableCell>
                    <TableCell className="px-3 text-ink-secondary">
                      {p.reversaAutomatica ? 'Sí, día 1' : 'No'}
                    </TableCell>
                    <TableCell className="px-3 text-ink-secondary">
                      {p.naceAutorizada ? 'Autorizadas' : 'En borrador'}
                    </TableCell>
                    <TableCell className="px-3">
                      <EstadoPlantillaBadge estado={p.estado} />
                    </TableCell>
                    <TableCell className="px-3 text-xs text-ink-secondary">{p.creadaPor}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>

      <PlantillaSheet
        open={nueva}
        onOpenChange={setNueva}
        onGuardada={(p: PlantillaRecurrente) => onAbrir(p.id)}
      />
    </div>
  );
}
