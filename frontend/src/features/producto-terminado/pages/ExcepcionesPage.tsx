import { useState } from 'react';
import { Breadcrumbs, EmptyState, ErrorState, PaginacionTabla, TableSkeleton } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { PageHeader } from '@/components/erp/PageHeader';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { MotivoDialog } from '@/components/erp/feedback/MotivoDialog';
import { Button } from '@/components/ui/button';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { mensajeErrorAccion } from '@/lib/api/error';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useEscaneoAccion, useEscaneos } from '../api/hooks';
import type { EscaneoDto, ResultadoEscaneo } from '../api/types';
import { textoMotivo } from '../lib/motivos-ui';

const PAGE_SIZE = 25;
const columnas =
  'grid grid-cols-[minmax(0,1.2fr)_100px_80px_90px_80px_90px_150px_minmax(0,1.6fr)_70px_minmax(0,1.4fr)] items-center';
const tabs: { valor: ResultadoEscaneo; label: string }[] = [
  { valor: 'Excepcion', label: 'Excepción' },
  { valor: 'Pospuesto', label: 'Pospuesto' },
  { valor: 'Reposicion', label: 'Reposición' },
];

export function ExcepcionesPage() {
  const [resultado, setResultado] = useState<ResultadoEscaneo>('Excepcion');
  const [offset, setOffset] = useState(0);
  const [descartando, setDescartando] = useState<EscaneoDto | null>(null);
  const [errorDialogo, setErrorDialogo] = useState<string | null>(null);
  const [errorAccion, setErrorAccion] = useState<string | null>(null);
  const gestionar = useAuthStore((s) => s.permisos.includes(P.ProductoTerminadoExcepcionesGestionar));
  const q = useEscaneos({ resultado, page: offset / PAGE_SIZE + 1, pageSize: PAGE_SIZE });
  const accion = useEscaneoAccion();
  const items = q.data?.items ?? [];

  const ejecutar = (e: EscaneoDto, ruta: 'reintentar' | 'revisar') => {
    setErrorAccion(null);
    accion.mutateAsync({ escaneo: e, ruta }).catch((err) => setErrorAccion(mensajeErrorAccion(err)));
  };

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 overflow-auto text-sm">
      <Breadcrumbs items={[{ label: 'Producto Terminado' }, { label: 'Excepciones de entrada' }]} />
      <PageHeader
        titulo="Excepciones de entrada"
        descripcion="Revise los escaneos que no pudieron darse de alta como pieza: reintente, descarte con motivo o marque como revisados."
      />
      {errorAccion && (
        <AvisoEnLinea tono="error" titulo="No se pudo completar la acción">
          {errorAccion}
        </AvisoEnLinea>
      )}
      {q.isError ? (
        <ErrorState
          title="No se pudieron cargar las excepciones"
          onRetry={() => {
            void q.refetch();
          }}
        />
      ) : (
        <div className="flex min-h-80 flex-1 flex-col">
          <ListadoModulo
            filtros={
              <Tabs
                value={resultado}
                onValueChange={(v) => {
                  setResultado(v as ResultadoEscaneo);
                  setOffset(0);
                  setErrorAccion(null);
                }}
              >
                <TabsList aria-label="Filtrar por resultado" className="gap-3 px-1">
                  {tabs.map((t) => (
                    <TabsTrigger key={t.valor} value={t.valor} aria-controls={undefined}>
                      {t.label}
                    </TabsTrigger>
                  ))}
                </TabsList>
              </Tabs>
            }
            pie={
              <PaginacionTabla
                total={q.data?.total ?? 0}
                offset={offset}
                limit={PAGE_SIZE}
                tamanos={[]}
                onChange={(o) => setOffset(o)}
              />
            }
          >
            {q.isLoading ? (
              <TableSkeleton />
            ) : items.length === 0 ? (
              <EmptyState title="Sin excepciones abiertas" />
            ) : (
              <Table className="grid min-w-0 text-sm">
                <TableHeader className="h-9 bg-surface-subtle text-left text-2xs uppercase text-ink-muted">
                  <TableRow className={`${columnas} h-9`}>
                    {['Etiqueta', 'Pedido', 'Posición', 'N.º de pieza', 'Estación', 'Área', 'Registrado', 'Motivo', 'Intentos', 'Acciones'].map(
                      (x) => (
                        <TableHead key={x} className="px-3">
                          {x}
                        </TableHead>
                      ),
                    )}
                  </TableRow>
                </TableHeader>
                <TableBody className="grid">
                  {items.map((e) => (
                    <TableRow key={e.id} className={`${columnas} min-h-12 border-b border-line-row`}>
                      <TableCell className="min-w-0 truncate px-3 font-mono text-xs">{e.etiqueta}</TableCell>
                      <TableCell className="px-3">{e.numeroPedido}</TableCell>
                      <TableCell className="px-3 tabular-nums">{e.posicion ?? '—'}</TableCell>
                      <TableCell className="px-3 tabular-nums">{e.numeroPieza ?? '—'}</TableCell>
                      <TableCell className="px-3 tabular-nums">{e.estacion}</TableCell>
                      <TableCell className="min-w-0 truncate px-3">{e.area ?? '—'}</TableCell>
                      <TableCell className="px-3 tabular-nums">
                        <DateTimeDisplay value={e.registradoAt} variant="datetime" />
                      </TableCell>
                      <TableCell className="min-w-0 whitespace-normal break-words px-3">{textoMotivo(e.motivo)}</TableCell>
                      <TableCell className="px-3 tabular-nums">{e.intentos}</TableCell>
                      <TableCell className="flex flex-wrap gap-1 px-3 py-1">
                        {gestionar && (
                          <>
                            {e.resultado !== 'Reposicion' && (
                              <Button
                                size="sm"
                                variant="secondary"
                                disabled={accion.isPending}
                                onClick={() => ejecutar(e, 'reintentar')}
                              >
                                Reintentar
                              </Button>
                            )}
                            {e.resultado === 'Reposicion' && (
                              <Button
                                size="sm"
                                variant="secondary"
                                disabled={accion.isPending}
                                onClick={() => ejecutar(e, 'revisar')}
                              >
                                Marcar revisada
                              </Button>
                            )}
                            <Button
                              size="sm"
                              variant="secondary-danger"
                              disabled={accion.isPending}
                              onClick={() => {
                                setErrorDialogo(null);
                                setDescartando(e);
                              }}
                            >
                              Descartar
                            </Button>
                          </>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </ListadoModulo>
        </div>
      )}
      {descartando && (
        <MotivoDialog
          titulo="Descartar escaneo"
          descripcion={`La etiqueta ${descartando.etiqueta} dejará de aparecer entre las excepciones abiertas.`}
          etiqueta="Motivo del descarte"
          min={3}
          max={500}
          verbo="Descartar"
          error={errorDialogo}
          onCerrar={() => setDescartando(null)}
          onConfirmar={async (motivo) => {
            setErrorDialogo(null);
            try {
              await accion.mutateAsync({ escaneo: descartando, ruta: 'descartar', body: { motivo } });
              setDescartando(null);
            } catch (err) {
              setErrorDialogo(mensajeErrorAccion(err));
            }
          }}
        />
      )}
    </main>
  );
}
