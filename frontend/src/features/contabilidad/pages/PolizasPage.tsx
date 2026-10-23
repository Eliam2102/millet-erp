import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { BarraFiltros, PaginacionTabla } from '@/components/erp';
import { TabsContabilidad } from '../components/TabsContabilidad';
import { ESTADOS_POLIZA } from '../lib/estados-ui';
import { PageHeader } from '@/components/erp/PageHeader';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useEffect, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { BookOpenCheck, Plus, Repeat, Scale, Search } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { cn } from '@/lib/utils';
import { useTiposDocumento } from '../api/dimensiones';
import { useEjercicios } from '../api/periodos';
import { usePolizas } from '../api/polizas';
import type { EstadoPoliza, Poliza } from '../api/polizas-types';
import { EstadoPolizaBadge } from '../components/polizas/EstadoPolizaBadge';
import { PolizaSheet } from '../components/polizas/PolizaSheet';
import { fechaCorta } from '../lib/dimensiones';
import { SELECT_CLASS } from '../lib/estilos';
import {
  etiquetaPeriodo,
  folioOBorrador,
  mensajePoliza,
  monto,
  nombrePeriodo,
  type PolizasSearch,
} from '../lib/polizas';

export const LIMITE_POLIZAS = 10;

const VISTAS = Object.entries(ESTADOS_POLIZA).map(([estado, ui]) => ({
  estado: estado as EstadoPoliza,
  label: ui.texto,
}));

interface Props {
  search: PolizasSearch;
  onSearch: (next: PolizasSearch) => void;
  /** Abre el detalle de una póliza (tras guardar un borrador nuevo). */
  onAbrir: (id: string) => void;
}

/** Bandeja de pólizas (P1, F1-CON-04): filtros por periodo, tipo, estado y búsqueda por folio o concepto. */
export function PolizasPage({ search, onSearch, onAbrir }: Props) {
  const puedeCapturar = useHasPermission(PermisosCanonicos.ContabilidadPolizasCapturar);
  const puedeVerRecurrentes = useHasPermission(PermisosCanonicos.ContabilidadRecurrentesLeer);
  const [nueva, setNueva] = useState(false);
  const [busqueda, setBusqueda] = useState(search.q ?? '');
  const q = useDebouncedValue(busqueda.trim(), 200);
  const offset = search.offset ?? 0;

  const tipos = useTiposDocumento(true);
  const ejercicios = useEjercicios({ retry: 0 });
  const polizas = usePolizas({
    anio: search.anio,
    numero: search.anio ? search.numero : undefined,
    tipoOperacionId: search.tipo,
    estado: search.estado,
    q: search.q || undefined,
    offset,
    limit: LIMITE_POLIZAS,
  });

  // La búsqueda escribe en la URL con debounce de 200 ms (Enter aplica de inmediato).
  useEffect(() => {
    if ((search.q ?? '') !== q) onSearch({ ...search, q: q || undefined, offset: undefined });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [q]);

  const cambiar = (cambio: Partial<PolizasSearch>) =>
    onSearch({ ...search, ...cambio, offset: undefined });
  const anios = (ejercicios.data ?? []).map((e) => e.anio).sort((a, b) => b - a);
  const items = polizas.data?.items ?? [];
  const total = polizas.data?.total ?? 0;

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <BookOpenCheck
              className="mr-2 inline-block size-5 text-brand"
              strokeWidth={1.6}
              aria-hidden="true"
            />
            Pólizas
          </>
        }
        descripcion={
          <>
            Captura de pólizas manuales, autorización con folio por tipo de operación y anulación
            con motivo.
          </>
        }
        acciones={
          <>
            <div className="ml-auto flex flex-wrap gap-2" data-print="hidden">
              <Button asChild variant="secondary">
                <Link to="/contabilidad/polizas/saldos">
                  <Scale className="mr-1 size-3.5" aria-hidden="true" />
                  Movimientos por cuenta
                </Link>
              </Button>
              {puedeVerRecurrentes && (
                <Button asChild variant="secondary">
                  <Link to="/contabilidad/polizas/recurrentes">
                    <Repeat className="mr-1 size-3.5" aria-hidden="true" />
                    Pólizas recurrentes
                  </Link>
                </Button>
              )}
              {puedeCapturar && (
                <Button onClick={() => setNueva(true)}>
                  <Plus className="mr-1 size-3.5" aria-hidden="true" />
                  Nueva póliza
                </Button>
              )}
            </div>
          </>
        }
      />

      <ListadoModulo
        tabs={
          <TabsContabilidad
            estados={VISTAS}
            valor={search.estado}
            onChange={(estado) => cambiar({ estado })}
          />
        }
        filtros={
          <BarraFiltros
            filtros={
              <>
                <label className="relative flex w-full sm:w-65">
                  <span className="sr-only">Buscar póliza</span>
                  <Search
                    className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-ink-muted"
                    aria-hidden="true"
                  />
                  <Input
                    type="search"
                    className="h-ctl-md pl-8"
                    placeholder="Folio (TT-000123) o concepto"
                    value={busqueda}
                    onChange={(e) => setBusqueda(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') cambiar({ q: busqueda.trim() || undefined });
                    }}
                  />
                </label>
                <label className="flex flex-col gap-1 text-xs font-medium text-ink-strong">
                  Ejercicio
                  <select
                    className={cn(SELECT_CLASS, 'h-ctl-md')}
                    value={search.anio ?? ''}
                    onChange={(e) =>
                      cambiar({
                        anio: e.target.value ? Number(e.target.value) : undefined,
                        numero: e.target.value ? search.numero : undefined,
                      })
                    }
                  >
                    <option value="">Todos</option>
                    {anios.map((a) => (
                      <option key={a} value={a}>
                        {a}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="flex flex-col gap-1 text-xs font-medium text-ink-strong">
                  Periodo
                  <select
                    className={cn(SELECT_CLASS, 'h-ctl-md')}
                    value={search.numero ?? ''}
                    disabled={!search.anio}
                    title={!search.anio ? 'Elige primero el ejercicio' : undefined}
                    onChange={(e) =>
                      cambiar({ numero: e.target.value ? Number(e.target.value) : undefined })
                    }
                  >
                    <option value="">Todos</option>
                    {Array.from({ length: 13 }, (_, i) => i + 1).map((n) => (
                      <option key={n} value={n}>
                        {String(n).padStart(2, '0')} {nombrePeriodo(n)}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="flex flex-col gap-1 text-xs font-medium text-ink-strong">
                  Tipo de operación
                  <select
                    className={cn(SELECT_CLASS, 'h-ctl-md')}
                    value={search.tipo ?? ''}
                    onChange={(e) => cambiar({ tipo: e.target.value || undefined })}
                  >
                    <option value="">Todos</option>
                    {(tipos.data ?? []).map((t) => (
                      <option key={t.id} value={t.id}>
                        {t.clave} — {t.nombre}
                      </option>
                    ))}
                  </select>
                </label>
                <span className="ml-auto self-center text-xs text-ink-muted">
                  {polizas.data ? `${total} ${total === 1 ? 'póliza' : 'pólizas'}` : ''}
                </span>
              </>
            }
          />
        }
        pie={
          <PaginacionTabla
            total={total}
            offset={offset}
            limit={LIMITE_POLIZAS}
            tamanos={[]}
            onChange={(offset) => onSearch({ ...search, offset: offset || undefined })}
          />
        }
      >
        {polizas.isLoading ? (
          <div className="flex flex-col gap-2 p-4" aria-label="Cargando pólizas">
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
          </div>
        ) : polizas.isError ? (
          <div role="alert" className="flex items-center gap-2 p-4 text-sm text-danger-fg">
            No se pudieron cargar las pólizas. {mensajePoliza(polizas.error)}
            <Button variant="ghost" size="sm" onClick={() => void polizas.refetch()}>
              Reintentar
            </Button>
          </div>
        ) : items.length === 0 ? (
          <p className="p-6 text-sm text-ink-muted">
            {search.q || search.anio || search.tipo || search.estado
              ? 'Ninguna póliza coincide con los filtros.'
              : 'Aún no hay pólizas. Captura la primera con «Nueva póliza».'}
          </p>
        ) : (
          <div className="overflow-x-auto">
            <Table className="w-full min-w-270 text-sm">
              <TableHeader className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
                <TableRow>
                  <TableHead className="h-9 px-3">Folio</TableHead>
                  <TableHead className="px-3">Tipo</TableHead>
                  <TableHead className="px-3">Fecha</TableHead>
                  <TableHead className="px-3">Periodo</TableHead>
                  <TableHead className="px-3">Concepto</TableHead>
                  <TableHead className="px-3 text-right">Cargos</TableHead>
                  <TableHead className="px-3 text-right">Abonos</TableHead>
                  <TableHead className="px-3">Estado</TableHead>
                  <TableHead className="px-3">Capturó</TableHead>
                  <TableHead className="px-3">Autorizó</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((p) => (
                  <TableRow key={p.id} className="h-12 border-b border-line-row">
                    <TableCell className="px-3">
                      <Link
                        to="/contabilidad/polizas/$id"
                        params={{ id: p.id }}
                        search={search}
                        className={cn(
                          'font-mono text-xs font-medium text-brand hover:underline',
                          !p.folio && 'font-sans',
                        )}
                        aria-label={`Ver póliza ${folioOBorrador(p.folio)}${p.folio ? '' : ` del ${fechaCorta(p.fecha)}`}`}
                      >
                        {folioOBorrador(p.folio)}
                      </Link>
                    </TableCell>
                    <TableCell className="px-3 font-mono text-xs">{p.tipoClave}</TableCell>
                    <TableCell className="whitespace-nowrap px-3 text-ink-secondary">
                      {fechaCorta(p.fecha)}
                    </TableCell>
                    <TableCell className="whitespace-nowrap px-3 text-ink-secondary">
                      {etiquetaPeriodo(p.periodoAnio, p.periodoNumero)}
                    </TableCell>
                    <TableCell className="max-w-80 truncate px-3" title={p.concepto}>
                      {p.concepto}
                    </TableCell>
                    <TableCell className="whitespace-nowrap px-3 text-right font-medium tabular-nums">
                      {monto(p.totalCargos)}
                    </TableCell>
                    <TableCell className="whitespace-nowrap px-3 text-right font-medium tabular-nums">
                      {monto(p.totalAbonos)}
                    </TableCell>
                    <TableCell className="px-3">
                      <EstadoPolizaBadge
                        estado={p.estado}
                        esAnulacion={p.esAnulacion}
                        relacion={p.relacion}
                        esRecurrente={p.esRecurrente}
                      />
                    </TableCell>
                    <TableCell className="px-3 text-xs text-ink-secondary">
                      {p.capturadaPor}
                    </TableCell>
                    <TableCell className="px-3 text-xs text-ink-secondary">
                      {p.autorizadaPor ?? '—'}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>

      <PolizaSheet open={nueva} onOpenChange={setNueva} onGuardada={(p: Poliza) => onAbrir(p.id)} />
    </div>
  );
}
