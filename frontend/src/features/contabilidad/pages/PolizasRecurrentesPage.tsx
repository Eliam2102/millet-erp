import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Play, Plus, Repeat } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useFormIdempotencyKey } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { cn } from '@/lib/utils';
import { useGenerarPendientes, usePlantillasRecurrentes } from '../api/recurrentes';
import type { EstadoPlantillaRecurrente, PlantillaRecurrente, ResultadoGeneracion as Resultado } from '../api/recurrentes-types';
import { EstadoPlantillaBadge } from '../components/recurrentes/EstadoPlantillaBadge';
import { PlantillaSheet } from '../components/recurrentes/PlantillaSheet';
import { ResultadoGeneracion } from '../components/recurrentes/ResultadoGeneracion';
import { monto } from '../lib/polizas';
import { etiquetaMes, mensajeRecurrente, type RecurrentesSearch } from '../lib/recurrentes';

const VISTAS: { estado: EstadoPlantillaRecurrente | undefined; label: string }[] = [
  { estado: undefined, label: 'Todas' },
  { estado: 'Borrador', label: 'Borrador' },
  { estado: 'Activa', label: 'Activas' },
  { estado: 'Suspendida', label: 'Suspendidas' },
  { estado: 'Terminada', label: 'Terminadas' },
];

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

  function onGenerar() {
    setMensaje(''); setResultado(null);
    generar.mutate({ idempotencyKey: keyGenerar }, { onSuccess: setResultado, onError: (e) => setMensaje(mensajeRecurrente(e)) });
  }

  return (
    <div className="flex flex-col gap-4 px-4 py-5 sm:px-6">
      <header className="flex flex-wrap items-start gap-3">
        <div className="min-w-0">
          <h1 className="flex items-center gap-2 text-3xl font-semibold">
            <Repeat className="size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Pólizas recurrentes
          </h1>
          <p className="text-sm text-ink-muted">
            Plantillas que generan la póliza de cada mes en su día, con reversa automática opcional el día 1 del mes siguiente.
          </p>
        </div>
        {puedeAdministrar && (
          <div className="ml-auto flex flex-wrap gap-2" data-print="hidden">
            <Button variant="secondary" onClick={onGenerar} disabled={generar.isPending}>
              <Play className="mr-1 size-3.5" aria-hidden="true" />{generar.isPending ? 'Generando…' : 'Generar pendientes'}
            </Button>
            <Button onClick={() => setNueva(true)}><Plus className="mr-1 size-3.5" aria-hidden="true" />Nueva plantilla</Button>
          </div>
        )}
      </header>

      {mensaje && <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">{mensaje}</p>}
      {resultado && <ResultadoGeneracion resultado={resultado} onCerrar={() => setResultado(null)} />}

      <section aria-label="Listado de plantillas" className="flex flex-col overflow-hidden rounded-t-lg bg-surface-card shadow-card-flat">
        <div role="tablist" aria-label="Vistas por estado" className="flex h-11 items-center gap-5 overflow-x-auto border-b border-line-divider px-4">
          {VISTAS.map((v) => {
            const activa = search.estado === v.estado;
            return (
              <button
                key={v.label} type="button" role="tab" aria-selected={activa}
                className={cn('h-full whitespace-nowrap text-sm', activa ? 'font-medium text-ink shadow-tab-active' : 'text-ink-muted')}
                onClick={() => onSearch({ ...search, estado: v.estado })}
              >
                {v.label}
                {activa && plantillas.data && <span className="ml-1.5 text-2xs tabular-nums text-brand">{items.length}</span>}
              </button>
            );
          })}
        </div>

        {plantillas.isLoading ? (
          <div className="flex flex-col gap-2 p-4" aria-label="Cargando plantillas">
            <Skeleton className="h-8 w-full" /><Skeleton className="h-8 w-full" /><Skeleton className="h-8 w-full" />
          </div>
        ) : plantillas.isError ? (
          <div role="alert" className="flex items-center gap-2 p-4 text-sm text-danger-fg">
            No se pudieron cargar las plantillas. {mensajeRecurrente(plantillas.error)}
            <Button variant="ghost" size="sm" onClick={() => void plantillas.refetch()}>Reintentar</Button>
          </div>
        ) : items.length === 0 ? (
          <p className="p-6 text-sm text-ink-muted">
            {search.estado ? 'Ninguna plantilla en este estado.' : 'Aún no hay plantillas. Prepara la primera con «Nueva plantilla».'}
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[980px] text-sm">
              <thead className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
                <tr>
                  <th className="h-9 px-3">Plantilla</th><th className="px-3">Tipo</th><th className="px-3">Inicio</th>
                  <th className="px-3 text-right">Generadas</th><th className="px-3 text-right">Importe</th><th className="px-3">Reversa</th>
                  <th className="px-3">Nacen</th><th className="px-3">Estado</th><th className="px-3">Preparó</th>
                </tr>
              </thead>
              <tbody>
                {items.map((p) => (
                  <tr key={p.id} className="h-12 border-b border-line-row">
                    <td className="max-w-[280px] truncate px-3">
                      <Link to="/contabilidad/polizas/recurrentes/$id" params={{ id: p.id }} search={search} className="font-medium text-brand hover:underline">
                        {p.nombre}
                      </Link>
                    </td>
                    <td className="px-3 font-mono text-xs">{p.tipoClave}</td>
                    <td className="whitespace-nowrap px-3 text-ink-secondary">{etiquetaMes(p.anioInicio, p.mesInicio)}</td>
                    <td className="whitespace-nowrap px-3 text-right tabular-nums">{p.generadas} de {p.numeroPeriodos}</td>
                    <td className="whitespace-nowrap px-3 text-right font-medium tabular-nums">{monto(p.importe)}</td>
                    <td className="px-3 text-ink-secondary">{p.reversaAutomatica ? 'Sí, día 1' : 'No'}</td>
                    <td className="px-3 text-ink-secondary">{p.naceAutorizada ? 'Autorizadas' : 'En borrador'}</td>
                    <td className="px-3"><EstadoPlantillaBadge estado={p.estado} /></td>
                    <td className="px-3 text-xs text-ink-secondary">{p.creadaPor}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {plantillas.data && items.length > 0 && (
          <div className="flex h-11 items-center border-t border-line-divider px-4 text-xs text-ink-muted" data-print="hidden">
            {items.length} {items.length === 1 ? 'plantilla' : 'plantillas'}
          </div>
        )}
      </section>

      <PlantillaSheet open={nueva} onOpenChange={setNueva} onGuardada={(p: PlantillaRecurrente) => onAbrir(p.id)} />
    </div>
  );
}
