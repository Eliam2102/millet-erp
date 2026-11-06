import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { PageHeader } from '@/components/erp/PageHeader';
import { BarraFiltros, EmptyState, ErrorState, PaginacionTabla, TableSkeleton } from '@/components/erp';
import { offsetLimitAPagina, paginaAOffsetLimit } from '@/components/erp/data/paginacion';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { esApiError } from '@/lib/api';
import { formatDate } from '@/lib/datetime';
import { useMisIncidencias, usePasosIncidencia, useResumenMisIncidencias } from '@/features/incidencias/api/useIncidencias';
import type { IncidenciaFila } from '@/features/incidencias/api/types';
import { EstadoIncidenciaBadge } from '@/features/incidencias/lib/estado';
import { fechaCompromisoTexto, folioIncidencia, origenIncidencia, vencida } from '@/features/incidencias/lib/formato';
import {
  VistaIncidencias,
  estadosDeVista,
  type IncidenciasSearch,
} from '@/features/incidencias/lib/search-schema';
import { nombreModulo } from '@/features/incidencias/lib/modulos';

const FROM = '/_app/incidencias/' as const;
const TODOS_MODULOS = '__todos__';

const VISTAS: { valor: VistaIncidencias; label: string }[] = [
  { valor: VistaIncidencias.Pendientes, label: 'Pendientes' },
  { valor: VistaIncidencias.Abierta, label: 'Abiertas' },
  { valor: VistaIncidencias.EnAtencion, label: 'En atención' },
  { valor: VistaIncidencias.Resuelta, label: 'Resueltas' },
  { valor: VistaIncidencias.Cancelada, label: 'Canceladas' },
  { valor: VistaIncidencias.Todas, label: 'Todas' },
];

/**
 * «Mis incidencias» (bandeja P2): las incidencias asignadas al usuario, de la más vieja a la más
 * nueva, con filtro por estado y módulo. El folio abre el detalle con la lista compacta a la izquierda.
 */
export function MisIncidenciasPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const resumen = useResumenMisIncidencias(true);
  const query = useMisIncidencias({
    estado: estadosDeVista(search.vista),
    modulo: search.modulo,
    ...paginaAOffsetLimit(search),
  });

  function actualizar(parcial: Partial<IncidenciasSearch>) {
    void navigate({ to: '/incidencias', search: { ...search, page: undefined, ...parcial } });
  }

  const conteos: Partial<Record<VistaIncidencias, number>> = resumen.data
    ? {
        [VistaIncidencias.Pendientes]: resumen.data.abiertas + resumen.data.enAtencion,
        [VistaIncidencias.Abierta]: resumen.data.abiertas,
        [VistaIncidencias.EnAtencion]: resumen.data.enAtencion,
      }
    : {};
  const vista = search.vista ?? VistaIncidencias.Pendientes;
  const items = query.data?.items ?? [];

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo="Mis incidencias"
        descripcion="Rechazos y pendientes asignados a ti: atiéndelos, resuélvelos o retoma el paso donde se detuvo el documento."
      />

      <div className="flex flex-col overflow-hidden rounded-t-lg bg-surface-card shadow-card-flat">
        <Tabs value={vista} onValueChange={(v) => actualizar({ vista: v === VistaIncidencias.Pendientes ? undefined : (v as VistaIncidencias) })}>
          <TabsList aria-label="Filtrar por estado" className="gap-3 px-4">
            {VISTAS.map((v) => (
              <TabsTrigger key={v.valor} value={v.valor} count={conteos[v.valor]} aria-controls={undefined}>
                {v.label}
              </TabsTrigger>
            ))}
          </TabsList>
        </Tabs>

        <BarraFiltros
          filtros={<FiltroModulo valor={search.modulo} onChange={(modulo) => actualizar({ modulo })} />}
          total={query.data ? `${query.data.total} ${query.data.total === 1 ? 'incidencia' : 'incidencias'}` : undefined}
          onLimpiar={search.modulo ? () => actualizar({ modulo: undefined }) : undefined}
        />

        {query.isError ? (
          <ErrorState
            title="No se pudieron cargar tus incidencias"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton rows={6} columns={[{ width: 'w-20' }, { width: 'w-48' }, { width: 'w-40' }, { width: 'w-24' }, { width: 'w-24' }]} />
        ) : items.length === 0 ? (
          <EmptyState
            title={vista === VistaIncidencias.Pendientes ? 'No tienes incidencias pendientes' : 'Sin incidencias'}
            description={
              search.modulo || vista !== VistaIncidencias.Pendientes
                ? 'No hay incidencias asignadas a ti con estos filtros.'
                : 'Cuando un paso se detenga y te toque atenderlo, aparecerá aquí.'
            }
          />
        ) : (
          <>
            <TablaIncidencias items={items} search={search} />
            <PaginacionTabla
              total={query.data?.total ?? 0}
              offset={query.data?.offset ?? 0}
              limit={query.data?.limit ?? paginaAOffsetLimit(search).limit}
              onChange={(offset, limit) => actualizar(offsetLimitAPagina(offset, limit))}
            />
          </>
        )}
      </div>
    </div>
  );
}

function FiltroModulo({ valor, onChange }: { valor?: string; onChange: (modulo: string | undefined) => void }) {
  const pasos = usePasosIncidencia();
  const modulos = [...new Set((pasos.data ?? []).map((p) => p.modulo))].sort();
  return (
    <Select value={valor ?? TODOS_MODULOS} onValueChange={(v) => onChange(v === TODOS_MODULOS ? undefined : v)}>
      <SelectTrigger className="h-ctl-md w-48 text-xs" aria-label="Módulo">
        <SelectValue placeholder="Módulo" />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value={TODOS_MODULOS}>Todos los módulos</SelectItem>
        {modulos.map((m) => (
          <SelectItem key={m} value={m}>
            {nombreModulo(m)}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

function TablaIncidencias({ items, search }: { items: IncidenciaFila[]; search: IncidenciasSearch }) {
  return (
    <TableContainer>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="w-28">Folio</TableHead>
            <TableHead>Paso y motivo</TableHead>
            <TableHead>Documento</TableHead>
            <TableHead className="w-28">Abierta</TableHead>
            <TableHead className="w-28">Compromiso</TableHead>
            <TableHead className="w-32">Estado</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {items.map((i) => (
            <TableRow key={i.id}>
              <TableCell>
                <Link
                  to="/incidencias/$id"
                  params={{ id: i.id }}
                  search={search}
                  className="font-mono text-xs font-medium text-brand hover:text-brand-hover"
                >
                  {folioIncidencia(i.numero)}
                </Link>
              </TableCell>
              <TableCell className="max-w-0">
                <p className="truncate text-sm text-ink">{i.pasoNombre}</p>
                <p className="truncate text-xs text-ink-muted" title={i.motivo}>
                  {i.motivo}
                </p>
              </TableCell>
              <TableCell>
                <p className="text-sm text-ink-secondary">{i.documentoEtiqueta}</p>
                <p className="text-2xs text-ink-muted">{nombreModulo(i.modulo)} · {origenIncidencia(i)}</p>
              </TableCell>
              <TableCell className="text-ink-secondary">{formatDate(i.abiertaEn)}</TableCell>
              <TableCell className={vencida(i) ? 'font-medium text-danger-fg' : 'text-ink-secondary'}>
                {fechaCompromisoTexto(i.fechaCompromiso)}
              </TableCell>
              <TableCell>
                <EstadoIncidenciaBadge estado={i.estado} />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
