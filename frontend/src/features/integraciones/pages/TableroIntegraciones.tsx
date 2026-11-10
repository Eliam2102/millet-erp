import { DateTimeDisplay, ErrorState, KpiCard, TableSkeleton } from '@/components/erp';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { problemaConsulta } from '@/components/erp/feedback/problema-consulta';
import { PageHeader } from '@/components/erp/PageHeader';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  EstadoMensajeIntegracion,
  TipoAlertaIntegracion,
  useAlertasIntegracion,
  useReintentarMensajeIntegracion,
  useTableroIntegraciones,
  type FuenteIntegracion,
  type MensajeIntegracion,
} from '@/features/integraciones/api/useTableroIntegraciones';
import { codigoSoporte, esApiError } from '@/lib/api';
import { useState } from 'react';
import { toast } from 'sonner';

type Tono = 'success' | 'warning' | 'danger' | 'info' | 'neutral';

const ESTADO_MENSAJE: Record<EstadoMensajeIntegracion, { texto: string; tono: Tono }> = {
  [EstadoMensajeIntegracion.Pendiente]: { texto: 'Pendiente', tono: 'info' },
  [EstadoMensajeIntegracion.Reintentando]: { texto: 'Reintentando', tono: 'warning' },
  [EstadoMensajeIntegracion.Error]: { texto: 'Error', tono: 'danger' },
  [EstadoMensajeIntegracion.Ok]: { texto: 'Procesado', tono: 'success' },
};

const TITULO_ALERTA: Record<TipoAlertaIntegracion, string> = {
  [TipoAlertaIntegracion.MensajesEnError]: 'Mensajes en error',
  [TipoAlertaIntegracion.FlujoDetenido]: 'Flujo detenido',
  [TipoAlertaIntegracion.ReintentosAltos]: 'Reintentos altos',
};

/** Estado de una integración completa, del más grave al más tranquilo. */
function estadoFuente(f: FuenteIntegracion): { texto: string; tono: Tono } {
  if (!f.disponible) return { texto: 'Sin lectura', tono: 'neutral' };
  if (f.detenido) return { texto: 'Detenida', tono: 'danger' };
  if (f.enError > 0) return { texto: 'Con errores', tono: 'danger' };
  if (f.reintentando > 0) return { texto: 'Reintentando', tono: 'warning' };
  return { texto: 'Al día', tono: 'success' };
}

/**
 * <c>&lt;TableroIntegraciones/&gt;</c> — I13.6: cada integración (eventos a Service
 * Bus, cola de errores, envíos a Almacenes, correo de CFDI) con sus pendientes,
 * reintentos, errores y último éxito; los mensajes de la elegida con su error y el
 * reintento manual; las alertas abiertas y el historial con su recuperación.
 */
export function TableroIntegraciones() {
  const tablero = useTableroIntegraciones();
  const [elegida, setElegida] = useState<string | null>(null);
  const datos = tablero.data;
  const fuentes = datos?.fuentes ?? [];
  const fuente =
    fuentes.find((f) => f.clave === elegida) ??
    fuentes.find((f) => estadoFuente(f).tono === 'danger') ??
    fuentes[0];

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        titulo={<>Tablero de integraciones</>}
        descripcion={
          <>
            Estado de cada integración del ERP: pendientes, reintentos, errores y alertas. Se
            actualiza cada 30 segundos.
          </>
        }
      />

      {tablero.isError ? (
        <ErrorState
          title="No se pudo cargar el tablero de integraciones"
          problem={problemaConsulta(tablero.error)}
          onRetry={() => void tablero.refetch()}
        />
      ) : tablero.isLoading || !datos ? (
        <TableSkeleton rows={5} />
      ) : (
        <>
          {datos.alertasAbiertas.map((a) => (
            <div key={a.id} data-testid={`alerta-${a.id}`}>
              <AvisoEnLinea
                tono={a.tipo === TipoAlertaIntegracion.MensajesEnError ? 'error' : 'advertencia'}
                titulo={`${a.fuenteNombre} · ${TITULO_ALERTA[a.tipo]}`}
              >
                {a.detalle} Abierta <DateTimeDisplay variant="datetime" value={a.abiertaEn} />
                {a.notificadaEn ? '; se avisó por correo.' : '; el correo aún no sale.'}
              </AvisoEnLinea>
            </div>
          ))}

          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <KpiCard
              etiqueta="Integraciones con alerta"
              valor={fuentes.filter((f) => estadoFuente(f).tono === 'danger').length}
              tono="danger"
              subdato={`de ${fuentes.length}`}
            />
            <KpiCard
              etiqueta="Mensajes en error"
              valor={fuentes.reduce((s, f) => s + f.enError, 0)}
              tono="danger"
            />
            <KpiCard
              etiqueta="Pendientes y en reintento"
              valor={fuentes.reduce((s, f) => s + f.pendientes + f.reintentando, 0)}
              tono="warning"
            />
            <KpiCard
              etiqueta="Procesados en 24 h"
              valor={fuentes.reduce((s, f) => s + f.okUltimas24h, 0)}
              tono="success"
            />
          </div>

          <section
            aria-label="Integraciones"
            className="overflow-x-auto rounded-lg bg-surface-card shadow-card-flat"
          >
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Integración</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead className="text-right">Pendientes</TableHead>
                  <TableHead className="text-right">Reintentando</TableHead>
                  <TableHead className="text-right">En error</TableHead>
                  <TableHead className="text-right">Procesados 24 h</TableHead>
                  <TableHead>Último éxito</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {fuentes.map((f) => {
                  const e = estadoFuente(f);
                  return (
                    <TableRow
                      key={f.clave}
                      data-testid={`fuente-${f.clave}`}
                      className={f.clave === fuente?.clave ? 'bg-surface-row-focus' : undefined}
                    >
                      <TableCell>
                        <button
                          type="button"
                          className="text-left font-medium text-brand"
                          onClick={() => setElegida(f.clave)}
                          aria-label={`Ver mensajes de ${f.nombre}`}
                        >
                          {f.nombre}
                        </button>
                        <p className="text-xs text-ink-muted">{f.grupo}</p>
                      </TableCell>
                      <TableCell>
                        <Badge variant={e.tono}>{e.texto}</Badge>
                        {f.errorLectura && (
                          <p className="mt-1 text-xs text-ink-muted">{f.errorLectura}</p>
                        )}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">{f.pendientes}</TableCell>
                      <TableCell className="text-right tabular-nums">{f.reintentando}</TableCell>
                      <TableCell className="text-right tabular-nums">{f.enError}</TableCell>
                      <TableCell className="text-right tabular-nums">{f.okUltimas24h}</TableCell>
                      <TableCell className="text-ink-secondary">
                        {f.ultimoExito ? (
                          <DateTimeDisplay variant="datetime" value={f.ultimoExito} />
                        ) : (
                          <span className="text-ink-muted">—</span>
                        )}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </section>

          {fuente && <MensajesFuente fuente={fuente} umbral={datos.umbralDetenidoMinutos} />}

          <HistorialAlertas />
        </>
      )}
    </main>
  );
}

function MensajesFuente({ fuente, umbral }: { fuente: FuenteIntegracion; umbral: number }) {
  return (
    <section aria-label={`Mensajes de ${fuente.nombre}`} className="flex flex-col gap-2">
      <div>
        <h2 className="font-medium">{fuente.nombre}</h2>
        <p className="text-xs text-ink-muted">
          Primero los que piden atención. Se considera detenida si un mensaje lleva más de {umbral}{' '}
          min sin procesarse.
          {!fuente.permiteReintento &&
            ' Esta integración no se reintenta desde aquí: revisa la causa en su módulo.'}
        </p>
      </div>
      {fuente.mensajes.length === 0 ? (
        <p className="text-ink-muted">Sin mensajes recientes.</p>
      ) : (
        <div className="overflow-x-auto rounded-lg bg-surface-card shadow-card-flat">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Mensaje</TableHead>
                <TableHead>Estado</TableHead>
                <TableHead className="text-right">Intentos</TableHead>
                <TableHead>Último error</TableHead>
                <TableHead>Creado</TableHead>
                <TableHead>Procesado</TableHead>
                <TableHead>
                  <span className="sr-only">Acciones</span>
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {fuente.mensajes.map((m) => (
                <FilaMensaje key={m.id} m={m} fuente={fuente} />
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </section>
  );
}

function FilaMensaje({ m, fuente }: { m: MensajeIntegracion; fuente: FuenteIntegracion }) {
  const reintentar = useReintentarMensajeIntegracion();
  const e = ESTADO_MENSAJE[m.estado];

  function onReintentar() {
    reintentar.mutate(
      { clave: fuente.clave, mensajeId: m.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () =>
          toast.success('El mensaje regresó a la cola; el sistema lo vuelve a intentar.'),
        onError: (error) =>
          esApiError(error)
            ? toast.error(error.problem.title, {
                description:
                  error.problem.detail && error.problem.detail !== error.problem.title
                    ? error.problem.detail
                    : codigoSoporte(error),
              })
            : toast.error(
                'No se pudo reintentar el mensaje. Revisa tu conexión y actualiza el tablero.',
              ),
      },
    );
  }

  return (
    <TableRow data-testid={`mensaje-${m.id}`} className="align-top">
      <TableCell className="font-mono text-xs">{m.referencia}</TableCell>
      <TableCell>
        <Badge variant={e.tono}>{e.texto}</Badge>
      </TableCell>
      <TableCell className="text-right tabular-nums">{m.intentos}</TableCell>
      <TableCell className="max-w-96 break-words text-xs">
        {m.ultimoError ?? <span className="text-ink-muted">—</span>}
      </TableCell>
      <TableCell className="text-ink-secondary">
        <DateTimeDisplay variant="datetime" value={m.creadoEn} />
      </TableCell>
      <TableCell className="text-ink-secondary">
        {m.procesadoEn ? (
          <DateTimeDisplay variant="datetime" value={m.procesadoEn} />
        ) : (
          <span className="text-ink-muted">—</span>
        )}
      </TableCell>
      <TableCell>
        {fuente.permiteReintento && m.estado === EstadoMensajeIntegracion.Error && (
          <Button
            size="sm"
            variant="outline"
            onClick={onReintentar}
            disabled={reintentar.isPending}
          >
            {reintentar.isPending ? 'Reintentando…' : 'Reintentar'}
          </Button>
        )}
      </TableCell>
    </TableRow>
  );
}

function HistorialAlertas() {
  const alertas = useAlertasIntegracion();
  const lista = alertas.data ?? [];
  return (
    <section aria-label="Historial de alertas" className="flex flex-col gap-2">
      <h2 className="font-medium">Historial de alertas</h2>
      {alertas.isError ? (
        <ErrorState
          title="No se pudo cargar el historial de alertas"
          problem={problemaConsulta(alertas.error)}
          onRetry={() => void alertas.refetch()}
        />
      ) : alertas.isLoading ? (
        <TableSkeleton rows={3} />
      ) : lista.length === 0 ? (
        <p className="text-ink-muted">Sin alertas registradas.</p>
      ) : (
        <div className="overflow-x-auto rounded-lg bg-surface-card shadow-card-flat">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Integración</TableHead>
                <TableHead>Alerta</TableHead>
                <TableHead>Abierta</TableHead>
                <TableHead>Estado</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {lista.map((a) => (
                <TableRow key={a.id} data-testid={`historial-${a.id}`}>
                  <TableCell>{a.fuenteNombre}</TableCell>
                  <TableCell>
                    <p>{TITULO_ALERTA[a.tipo]}</p>
                    <p className="text-xs text-ink-muted">{a.detalle}</p>
                  </TableCell>
                  <TableCell className="text-ink-secondary">
                    <DateTimeDisplay variant="datetime" value={a.abiertaEn} />
                  </TableCell>
                  <TableCell>
                    {a.cerradaEn ? (
                      <>
                        <Badge variant="success">Recuperada</Badge>
                        <p className="mt-1 text-xs text-ink-muted">
                          <DateTimeDisplay variant="datetime" value={a.cerradaEn} />
                        </p>
                      </>
                    ) : (
                      <Badge variant="danger">Abierta</Badge>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </section>
  );
}
