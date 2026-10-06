import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { CalendarRange, Plus, TriangleAlert } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { Textarea } from '@/components/ui/textarea';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { formatDateTime } from '@/lib/datetime';
import {
  MOTIVO_MAXIMO, MOTIVO_MINIMO, periodoKeys, useAbrirPeriodos, useBitacoraPeriodo, useCrearEjercicio, useEjercicios, useTransicionPeriodo,
  type AccionPeriodo, type EjercicioContable, type EstadoPeriodo, type PeriodoContable,
} from '../api/periodos';
import { fechaCorta } from '../lib/dimensiones';
import { SELECT_CLASS } from '../lib/estilos';

type VarianteBadge = 'success' | 'info' | 'neutral';

const ETIQUETA_ESTADO: Record<EstadoPeriodo, string> = { NoAbierto: 'No abierto', Abierto: 'Abierto', Cerrado: 'Cerrado' };
const VARIANTE_ESTADO: Record<EstadoPeriodo, VarianteBadge> = { NoAbierto: 'neutral', Abierto: 'info', Cerrado: 'success' };
const ETIQUETA_ACCION: Record<AccionPeriodo, string> = { Abrir: 'Apertura', Cerrar: 'Cierre', Reabrir: 'Reapertura' };

/** Error del servidor → mensaje para el usuario. `recargar` cuando los datos en pantalla ya no son los vigentes. */
function mensajeErrorPeriodo(e: unknown): { mensaje: string; recargar: boolean } {
  if (!esApiError(e)) return { mensaje: 'No se pudo completar la operación (sin conexión o error inesperado). Reintenta.', recargar: false };
  // Versión vieja (409 CONCURRENCY_CONFLICT) o sin If-Match (428).
  if (e.status === 428 || (e.status === 409 && e.code === 'CONCURRENCY_CONFLICT')) return { mensaje: 'Otro usuario modificó este periodo; recargue.', recargar: true };
  const detalle = e.problem.detail ?? e.problem.title;
  if (e.status === 403) return { mensaje: detalle || 'No tienes permiso para esta acción.', recargar: false };
  return { mensaje: detalle, recargar: e.status === 409 };
}

function etiquetaPeriodo(p: Pick<PeriodoContable, 'numero' | 'nombre'>): string {
  return `${p.numero} · ${p.nombre}`;
}

/** Quién y cuándo cerró o reabrió (lo más reciente que explica el estado actual). */
function responsable(p: PeriodoContable): string[] {
  const lineas: string[] = [];
  if (p.estado === 'Cerrado' && p.cerradoPor) lineas.push(`Cerró ${p.cerradoPor} · ${formatDateTime(p.cerradoEn!)}`);
  if (p.reabiertoPor) lineas.push(`Reabrió ${p.reabiertoPor} · ${formatDateTime(p.reabiertoEn!)}`);
  if (lineas.length === 0 && p.abiertoPor) lineas.push(`Abrió ${p.abiertoPor} · ${formatDateTime(p.abiertoEn!)}`);
  return lineas;
}

/** F1-CON-03: ejercicio contable con 12 periodos + 13 de ajustes; apertura, cierre y reapertura con motivo y bitácora. */
export function PeriodosPage() {
  const puedeAdministrar = useHasPermission(PermisosCanonicos.ContabilidadPeriodoAdministrar);
  const puedeCerrar = useHasPermission(PermisosCanonicos.ContabilidadPeriodoCerrar);
  const puedeReabrir = useHasPermission(PermisosCanonicos.ContabilidadPeriodoReabrir);
  const ejercicios = useEjercicios();
  const [anioElegido, setAnioElegido] = useState<number | null>(null);
  const [seleccion, setSeleccion] = useState<number[]>([]);
  const [nuevo, setNuevo] = useState(false);
  const [abriendo, setAbriendo] = useState<number[] | null>(null);
  const [transicion, setTransicion] = useState<{ periodo: PeriodoContable; accion: 'cerrar' | 'reabrir' } | null>(null);
  const [bitacoraDe, setBitacoraDe] = useState<string | null>(null);

  const lista = ejercicios.data ?? [];
  const ejercicio = lista.find((e) => e.anio === anioElegido) ?? lista[0];
  const periodos = ejercicio?.periodos ?? [];
  const periodo12 = periodos.find((p) => p.numero === 12);
  const conBitacora = periodos.find((p) => p.id === bitacoraDe) ?? null;
  const hayAcciones = puedeAdministrar || puedeCerrar || puedeReabrir;

  function elegirAnio(anio: number) {
    setAnioElegido(anio);
    setSeleccion([]);
    setBitacoraDe(null);
  }

  function alternar(numero: number, marcado: boolean) {
    setSeleccion((s) => (marcado ? [...s, numero] : s.filter((n) => n !== numero)));
  }

  /** El 13 solo se abre con el 12 cerrado (D3); el servidor lo vuelve a exigir. */
  function bloqueoApertura(p: PeriodoContable): string | undefined {
    return p.numero === 13 && periodo12?.estado !== 'Cerrado' ? 'El periodo 13 se abre después de cerrar diciembre' : undefined;
  }

  return (
    <div className="flex flex-col gap-4 px-6 py-5">
      <header className="flex flex-wrap items-center gap-2">
        <div>
          <h1 className="flex items-center gap-2 text-3xl font-semibold">
            <CalendarRange className="size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Periodos contables
          </h1>
          <p className="text-sm text-ink-muted">Apertura, cierre y reapertura de los periodos del ejercicio, con motivo y bitácora de cada cambio.</p>
        </div>
        {puedeAdministrar && (
          <Button className="ml-auto" onClick={() => setNuevo(true)} data-print="hidden">
            <Plus className="mr-1 size-4" aria-hidden="true" />Nuevo ejercicio
          </Button>
        )}
      </header>

      {ejercicios.isLoading ? (
        <div className="space-y-2" aria-label="Cargando ejercicios"><Skeleton className="h-8 w-60" /><Skeleton className="h-64 w-full" /></div>
      ) : ejercicios.isError ? (
        <div role="alert" className="flex items-center gap-2 text-sm text-danger-fg">
          No se pudieron cargar los ejercicios.
          <Button variant="ghost" size="sm" onClick={() => void ejercicios.refetch()}>Reintentar</Button>
        </div>
      ) : !ejercicio ? (
        <p className="rounded-lg bg-surface-card p-6 text-sm text-ink-muted shadow-card-flat">
          No hay ejercicios contables. {puedeAdministrar ? 'Crea el ejercicio del año para generar sus 13 periodos.' : 'Pide a Contabilidad que cree el ejercicio del año.'}
        </p>
      ) : (
        <>
          <div className="flex flex-wrap items-end gap-3" data-print="hidden">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="f-ejercicio">Ejercicio</Label>
              <select id="f-ejercicio" className={SELECT_CLASS} value={ejercicio.anio} onChange={(e) => elegirAnio(Number(e.target.value))}>
                {lista.map((e) => <option key={e.id} value={e.anio}>{e.anio}</option>)}
              </select>
            </div>
            {puedeAdministrar && (
              <Button
                variant="outline" className="ml-auto" disabled={seleccion.length === 0}
                title={seleccion.length === 0 ? 'Marca los periodos sin abrir que quieras abrir' : undefined}
                onClick={() => setAbriendo([...seleccion].sort((a, b) => a - b))}
              >
                Abrir seleccionados{seleccion.length > 0 ? ` (${seleccion.length})` : ''}
              </Button>
            )}
          </div>

          <div className="overflow-hidden rounded-lg bg-surface-card shadow-card-flat">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[880px] text-sm">
                <thead className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
                  <tr>
                    {puedeAdministrar && <th className="w-9 px-3 py-2"><span className="sr-only">Seleccionar</span></th>}
                    <th className="px-3 py-2">Periodo</th><th className="px-3 py-2">Fechas</th><th className="px-3 py-2">Estado</th>
                    <th className="px-3 py-2">Responsable</th>
                    <th className="px-3 py-2"><span className="sr-only">Acciones</span></th>
                  </tr>
                </thead>
                <tbody>
                  {periodos.map((p) => {
                    const bloqueo = bloqueoApertura(p);
                    return (
                      <tr key={p.id} className={`border-b border-line-row ${p.id === bitacoraDe ? 'bg-surface-row-focus' : ''}`}>
                        {puedeAdministrar && (
                          <td className="px-3 py-2">
                            {p.estado === 'NoAbierto' && !bloqueo && (
                              <Checkbox
                                aria-label={`Seleccionar ${etiquetaPeriodo(p)}`} checked={seleccion.includes(p.numero)}
                                onCheckedChange={(v) => alternar(p.numero, v === true)}
                              />
                            )}
                          </td>
                        )}
                        <td className="px-3 py-2 font-medium">{etiquetaPeriodo(p)}</td>
                        <td className="px-3 py-2 text-ink-secondary">
                          {p.fechaInicio === p.fechaFin ? fechaCorta(p.fechaInicio) : `${fechaCorta(p.fechaInicio)} – ${fechaCorta(p.fechaFin)}`}
                        </td>
                        <td className="px-3 py-2"><Badge variant={VARIANTE_ESTADO[p.estado]}>{ETIQUETA_ESTADO[p.estado]}</Badge></td>
                        <td className="px-3 py-2 text-xs text-ink-muted">
                          {responsable(p).map((l) => <span key={l} className="block">{l}</span>)}
                        </td>
                        <td className="px-3 py-2 text-right">
                          <span className="flex justify-end gap-1">
                            {puedeAdministrar && p.estado === 'NoAbierto' && (
                              <Button variant="outline" size="sm" disabled={!!bloqueo} title={bloqueo} onClick={() => setAbriendo([p.numero])}>
                                Abrir
                              </Button>
                            )}
                            {puedeCerrar && p.estado === 'Abierto' && (
                              <Button variant="outline" size="sm" onClick={() => setTransicion({ periodo: p, accion: 'cerrar' })}>Cerrar</Button>
                            )}
                            {puedeReabrir && p.estado === 'Cerrado' && (
                              <Button variant="secondary-danger" size="sm" onClick={() => setTransicion({ periodo: p, accion: 'reabrir' })}>Reabrir</Button>
                            )}
                            <Button
                              variant="ghost" size="sm" aria-pressed={p.id === bitacoraDe}
                              aria-label={`Bitácora de ${etiquetaPeriodo(p)}`}
                              onClick={() => setBitacoraDe(p.id === bitacoraDe ? null : p.id)}
                            >
                              Bitácora
                            </Button>
                          </span>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
            {!hayAcciones && (
              <p className="border-t border-line-divider px-4 py-2.5 text-xs text-ink-muted">Solo consulta: no tienes permisos para abrir, cerrar ni reabrir periodos.</p>
            )}
          </div>

          {conBitacora && <BitacoraPanel periodo={conBitacora} />}
        </>
      )}

      {puedeAdministrar && (
        <NuevoEjercicioDialog
          open={nuevo}
          sugerido={lista.length ? Math.max(...lista.map((e) => e.anio)) + 1 : new Date().getFullYear()}
          onClose={(anio) => { setNuevo(false); if (anio) elegirAnio(anio); }}
        />
      )}
      {puedeAdministrar && ejercicio && (
        <AbrirDialog ejercicio={ejercicio} numeros={abriendo} onClose={(ok) => { setAbriendo(null); if (ok) setSeleccion([]); }} />
      )}
      <MotivoDialog transicion={transicion} onClose={() => setTransicion(null)} />
    </div>
  );
}

/** Error de una acción; con «Recargar» se vuelven a pedir los periodos y se cierra el diálogo. */
function ErrorAccion({ error, onRecargar }: { error: { mensaje: string; recargar: boolean } | null; onRecargar: () => void }) {
  const qc = useQueryClient();
  if (!error) return null;
  return (
    <div role="alert" className="flex flex-wrap items-center gap-2 text-sm text-danger-fg">
      {error.mensaje}
      {error.recargar && (
        <Button variant="ghost" size="sm" onClick={() => { void qc.invalidateQueries({ queryKey: periodoKeys.all }); onRecargar(); }}>Recargar</Button>
      )}
    </div>
  );
}

function NuevoEjercicioDialog({ open, sugerido, onClose }: { open: boolean; sugerido: number; onClose: (anio?: number) => void }) {
  const crear = useCrearEjercicio();
  const keyFor = useBodyScopedIdempotencyKey();
  const [anio, setAnio] = useState('');
  const [error, setError] = useState<{ mensaje: string; recargar: boolean } | null>(null);
  const valor = anio || String(sugerido);

  function cerrar(creado?: number) {
    setAnio('');
    setError(null);
    onClose(creado);
  }

  function confirmar() {
    setError(null);
    const command = { anio: Number(valor) };
    crear.mutate({ ...command, idempotencyKey: keyFor(command) }, { onSuccess: (e) => cerrar(e.anio), onError: (e) => setError(mensajeErrorPeriodo(e)) });
  }

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) cerrar(); }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Nuevo ejercicio</DialogTitle>
          <DialogDescription>
            Se generan los 12 periodos mensuales y el 13 de ajustes de auditoría, todos sin abrir. Después abre los que vayan a operarse.
          </DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="nuevo-anio">Año</Label>
          <Input id="nuevo-anio" type="number" min={2000} max={2999} className="w-32" value={valor} onChange={(e) => setAnio(e.target.value)} />
        </div>
        <ErrorAccion error={error} onRecargar={() => cerrar()} />
        <DialogFooter>
          <Button variant="ghost" onClick={() => cerrar()}>Cancelar</Button>
          <Button onClick={confirmar} disabled={crear.isPending || !/^\d{4}$/.test(valor)}>
            {crear.isPending ? 'Creando…' : 'Crear ejercicio'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function AbrirDialog({ ejercicio, numeros, onClose }: { ejercicio: EjercicioContable; numeros: number[] | null; onClose: (ok: boolean) => void }) {
  const abrir = useAbrirPeriodos();
  const keyFor = useBodyScopedIdempotencyKey();
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<{ mensaje: string; recargar: boolean } | null>(null);
  const nombres = (numeros ?? []).map((n) => ejercicio.periodos.find((p) => p.numero === n)).filter((p) => !!p).map(etiquetaPeriodo);

  function cerrar(ok: boolean) {
    setMotivo('');
    setError(null);
    onClose(ok);
  }

  function confirmar() {
    if (!numeros) return;
    setError(null);
    const command = { ejercicioId: ejercicio.id, version: ejercicio.version, numeros, motivo: motivo.trim() || null };
    abrir.mutate({ ejercicio, numeros, motivo, idempotencyKey: keyFor(command) }, { onSuccess: () => cerrar(true), onError: (e) => setError(mensajeErrorPeriodo(e)) });
  }

  return (
    <Dialog open={!!numeros} onOpenChange={(o) => { if (!o) cerrar(false); }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{nombres.length > 1 ? `Abrir ${nombres.length} periodos` : 'Abrir periodo'}</DialogTitle>
          <DialogDescription>
            Ejercicio {ejercicio.anio}: {nombres.join(', ')}. Al abrirse admiten movimientos con fecha dentro del periodo.
            {nombres.length > 1 && ' Si alguno no puede abrirse, no se abre ninguno.'}
          </DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="abrir-motivo">Motivo <span className="font-normal text-ink-muted">(opcional)</span></Label>
          <Textarea id="abrir-motivo" maxLength={MOTIVO_MAXIMO} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
        </div>
        <ErrorAccion error={error} onRecargar={() => cerrar(false)} />
        <DialogFooter>
          <Button variant="ghost" onClick={() => cerrar(false)}>Cancelar</Button>
          <Button onClick={confirmar} disabled={abrir.isPending}>{abrir.isPending ? 'Abriendo…' : nombres.length > 1 ? 'Abrir periodos' : 'Abrir periodo'}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** Cerrar y reabrir: motivo obligatorio de 10 a 500 caracteres (D7), queda en la bitácora. */
function MotivoDialog({ transicion, onClose }: {
  transicion: { periodo: PeriodoContable; accion: 'cerrar' | 'reabrir' } | null;
  onClose: () => void;
}) {
  const mutacion = useTransicionPeriodo();
  const keyFor = useBodyScopedIdempotencyKey();
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<{ mensaje: string; recargar: boolean } | null>(null);
  const largo = motivo.trim().length;
  const motivoValido = largo >= MOTIVO_MINIMO && largo <= MOTIVO_MAXIMO;
  const reabrir = transicion?.accion === 'reabrir';
  const nombre = transicion ? `${etiquetaPeriodo(transicion.periodo)} ${transicion.periodo.anio}` : '';

  function cerrar() {
    setMotivo('');
    setError(null);
    onClose();
  }

  function confirmar() {
    if (!transicion || !motivoValido) return;
    setError(null);
    const command = { periodoId: transicion.periodo.id, version: transicion.periodo.version, accion: transicion.accion, motivo: motivo.trim() };
    mutacion.mutate({ ...transicion, motivo, idempotencyKey: keyFor(command) }, { onSuccess: cerrar, onError: (e) => setError(mensajeErrorPeriodo(e)) });
  }

  return (
    <Dialog open={!!transicion} onOpenChange={(o) => { if (!o) cerrar(); }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{reabrir ? `Reabrir ${nombre}` : `Cerrar ${nombre}`}</DialogTitle>
          <DialogDescription>
            {reabrir
              ? 'El periodo vuelve a admitir movimientos. Solo se reabre si el periodo siguiente no está cerrado.'
              : 'El periodo deja de admitir movimientos. Los periodos se cierran en orden: los anteriores deben estar cerrados.'}
          </DialogDescription>
        </DialogHeader>
        {reabrir && (
          <div role="note" className="flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg">
            <TriangleAlert className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} aria-hidden="true" />
            Reabrir la contabilidad no reabre el inventario: el cierre de Almacén se mantiene.
          </div>
        )}
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="transicion-motivo">Motivo</Label>
          <Textarea
            id="transicion-motivo" rows={3} maxLength={MOTIVO_MAXIMO} value={motivo} aria-describedby="transicion-motivo-ayuda"
            aria-invalid={largo > 0 && !motivoValido} onChange={(e) => setMotivo(e.target.value)}
          />
          <p id="transicion-motivo-ayuda" className="text-xs text-ink-muted">
            Obligatorio, de {MOTIVO_MINIMO} a {MOTIVO_MAXIMO} caracteres ({largo}). Queda en la bitácora del periodo.
          </p>
        </div>
        <ErrorAccion error={error} onRecargar={cerrar} />
        <DialogFooter>
          <Button variant="ghost" onClick={cerrar}>Cancelar</Button>
          <Button
            variant={reabrir ? 'secondary-danger' : 'default'} onClick={confirmar} disabled={!motivoValido || mutacion.isPending}
            title={motivoValido ? undefined : `Escribe un motivo de al menos ${MOTIVO_MINIMO} caracteres`}
          >
            {mutacion.isPending ? 'Guardando…' : reabrir ? 'Reabrir periodo' : 'Cerrar periodo'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function BitacoraPanel({ periodo }: { periodo: PeriodoContable }) {
  const bitacora = useBitacoraPeriodo(periodo.id);
  return (
    <section aria-label={`Bitácora de ${etiquetaPeriodo(periodo)}`} className="flex flex-col gap-2 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="text-md font-semibold">Bitácora · {etiquetaPeriodo(periodo)} {periodo.anio}</h2>
      {bitacora.isLoading ? (
        <Skeleton className="h-8 w-full" />
      ) : bitacora.isError ? (
        <div role="alert" className="flex items-center gap-2 text-sm text-danger-fg">
          No se pudo cargar la bitácora.
          <Button variant="ghost" size="sm" onClick={() => void bitacora.refetch()}>Reintentar</Button>
        </div>
      ) : bitacora.data!.length === 0 ? (
        <p className="text-sm text-ink-muted">Sin movimientos: el periodo no se ha abierto.</p>
      ) : (
        <table className="w-full text-sm">
          <thead className="border-b border-line-divider text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
            <tr><th className="px-3 py-2">Cuándo</th><th className="px-3 py-2">Quién</th><th className="px-3 py-2">Acción</th><th className="px-3 py-2">Motivo</th></tr>
          </thead>
          <tbody>
            {[...bitacora.data!].reverse().map((b) => (
              <tr key={b.id} className="border-b border-line-row">
                <td className="whitespace-nowrap px-3 py-2 text-ink-secondary">{formatDateTime(b.ocurridoEn)}</td>
                <td className="px-3 py-2">{b.usuarioNombre}</td>
                <td className="px-3 py-2">
                  {ETIQUETA_ACCION[b.accion]}
                  <span className="block text-xs text-ink-muted">{ETIQUETA_ESTADO[b.estadoAnterior]} → {ETIQUETA_ESTADO[b.estadoNuevo]}</span>
                </td>
                <td className="px-3 py-2 text-ink-secondary">{b.motivo ?? <span className="text-ink-muted">Sin motivo</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
