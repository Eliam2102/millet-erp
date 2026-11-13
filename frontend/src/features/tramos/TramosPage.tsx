import { useState } from 'react';
import { toast } from 'sonner';
import { Breadcrumbs, EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { MotivoDialog } from '@/components/erp/feedback/MotivoDialog';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableContainer, TableHead, TableHeader, TableRow,
} from '@/components/ui/table';
import { useNombreUsuario } from '@/features/producto-terminado/lib/usuarios';
import { mensajeApi } from '@/features/producto-terminado/lib/errores';
import { useAccionTramo, useTramos, type TramoDto } from './api';

/** Tramos de la operación (M-303): detener frena las escrituras del tramo (409 TRAMO_DETENIDO); reanudar lo hace otra persona, con motivo. */
export function TramosPage() {
  const q = useTramos();
  const accion = useAccionTramo();
  const nombreUsuario = useNombreUsuario();
  const [dialogo, setDialogo] = useState<{ t: TramoDto; a: 'detener' | 'reanudar' } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const items = q.data ?? [];

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 overflow-auto text-sm">
      <Breadcrumbs items={[{ label: 'Administración' }, { label: 'Tramos' }]} />
      <PageHeader
        titulo="Tramos de la operación"
        descripcion="Detén un tramo cuando algo falla para que no se acumulen errores; quien lo reanuda debe ser otra persona y dejar su motivo."
      />
      {q.isError ? (
        <ErrorState title="No se pudieron cargar los tramos" onRetry={() => void q.refetch()} />
      ) : q.isLoading ? (
        <TableSkeleton />
      ) : items.length === 0 ? (
        <EmptyState title="No hay tramos configurados." />
      ) : (
        <TableContainer maxHeightClassName="" className="min-w-0" role="region" aria-label="Tramos" tabIndex={0}>
          <Table paginada>
            <TableHeader>
              <TableRow>
                <TableHead>Tramo</TableHead>
                <TableHead>Módulo</TableHead>
                <TableHead>Estado</TableHead>
                <TableHead>Detalle</TableHead>
                <TableHead>Último evento correcto</TableHead>
                <TableHead>Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((t) => (
                <TableRow key={t.clave}>
                  <TableCell>
                    <p className="font-medium">{t.nombre}</p>
                    <p className="font-mono text-xs text-ink-muted">{t.clave}</p>
                  </TableCell>
                  <TableCell>{t.modulo}</TableCell>
                  <TableCell><Badge variant={t.detenido ? 'danger' : 'success'}>{t.detenido ? 'Detenido' : 'Operando'}</Badge></TableCell>
                  <TableCell className="max-w-96 whitespace-normal break-words text-xs">
                    {t.detenido ? (
                      <>
                        <p>{t.motivo}</p>
                        <p className="text-ink-muted">
                          {nombreUsuario(t.detenidoPorId)}{t.detenidoAt && (
                            <>
                              {' '}
                              · <DateTimeDisplay value={t.detenidoAt} variant="datetime" /></>
                          )}
                        </p>
                      </>
                    ) : t.reanudadoAt ? (
                      <>
                        <p className="text-ink-muted">Reanudado por {nombreUsuario(t.reanudadoPorId)} ·{' '}
                          <DateTimeDisplay value={t.reanudadoAt} variant="datetime" /></p>
                        {t.motivoReanudacion && <p>{t.motivoReanudacion}</p>}
                      </>
                    ) : (
                      <span className="text-ink-muted">—</span>
                    )}
                  </TableCell>
                  <TableCell className="text-xs">
                    {t.ultimoEventoCorrectoAt ? (
                      <>
                        <DateTimeDisplay value={t.ultimoEventoCorrectoAt} variant="datetime" />
                        {t.ultimoEventoCorrecto && (
                          <p className="text-ink-muted">{t.ultimoEventoCorrecto}</p>
                        )}
                      </>
                    ) : (
                      <span className="text-ink-muted">Sin eventos todavía</span>
                    )}
                  </TableCell>
                  <TableCell>
                    <Button size="sm" variant="outline" onClick={() => { setError(null); setDialogo({ t, a: t.detenido ? 'reanudar' : 'detener' }); }}>
                      {t.detenido ? 'Reanudar' : 'Detener'}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}
      {dialogo && (
        <MotivoDialog
          titulo={dialogo.a === 'detener' ? `Detener ${dialogo.t.nombre}` : `Reanudar ${dialogo.t.nombre}`
          }
          descripcion={
            dialogo.a === 'detener'
              ? 'Mientras esté detenido, las escrituras del tramo se rechazan. Las consultas siguen funcionando.'
              : 'Reanudar lo debe hacer una persona distinta a la que detuvo el tramo. Indica qué se corrigió.'
          }
          etiqueta={dialogo.a === 'detener' ? 'Motivo de la detención' : 'Motivo de la reanudación'}
          min={5}
          max={500}
          verbo={dialogo.a === 'detener' ? 'Detener' : 'Reanudar'}
          variante={dialogo.a === 'detener' ? 'secondary-danger' : 'default'}
          error={error}
          onCerrar={() => setDialogo(null)}
          onConfirmar={async (motivo) => {
            setError(null);
            try {
              await accion.mutateAsync({ clave: dialogo.t.clave, accion: dialogo.a, motivo });
              toast.success(dialogo.a === 'detener' ? 'Tramo detenido' : 'Tramo reanudado');
              setDialogo(null);
            } catch (e) {
              setError(mensajeApi(e));
            }
          }}
        />
      )}
    </main>
  );
}
