import { KpiCard } from '@/components/erp/display/KpiCard';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { BarraFiltros, PaginacionTabla } from '@/components/erp';
import { TabsContabilidad } from '../components/TabsContabilidad';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { ESTADOS_PERIODO } from '../lib/estados-ui';
import { PageHeader } from '@/components/erp/PageHeader';
import { DialogBody } from '@/components/ui/dialog';
import { SheetBody } from '@/components/ui/sheet';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { ArrowRight, CalendarRange, History, Plus, TriangleAlert } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Sheet,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Textarea } from '@/components/ui/textarea';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { formatDateTime } from '@/lib/datetime';
import {
  MOTIVO_MAXIMO,
  MOTIVO_MINIMO,
  periodoKeys,
  useAbrirPeriodos,
  useBitacoraPeriodo,
  useCrearEjercicio,
  useEjercicios,
  useTransicionPeriodo,
  type AccionPeriodo,
  type EjercicioContable,
  type EstadoPeriodo,
  type PeriodoContable,
} from '../api/periodos';
import { fechaCorta } from '../lib/dimensiones';
import { SELECT_CLASS } from '../lib/estilos';

const ETIQUETA_ACCION: Record<AccionPeriodo, string> = {
  Abrir: 'Apertura',
  Cerrar: 'Cierre',
  Reabrir: 'Reapertura',
};

/** Error del servidor → mensaje para el usuario. `recargar` cuando los datos en pantalla ya no son los vigentes. */
function mensajeErrorPeriodo(e: unknown): { mensaje: string; recargar: boolean } {
  if (!esApiError(e))
    return {
      mensaje: 'No se pudo completar la operación (sin conexión o error inesperado). Reintenta.',
      recargar: false,
    };
  // Versión vieja (409 CONCURRENCY_CONFLICT) o sin If-Match (428).
  if (e.status === 428 || (e.status === 409 && e.code === 'CONCURRENCY_CONFLICT'))
    return { mensaje: 'Otro usuario modificó este periodo; recargue.', recargar: true };
  const detalle = e.problem.detail ?? e.problem.title;
  if (e.status === 403)
    return { mensaje: detalle || 'No tienes permiso para esta acción.', recargar: false };
  return { mensaje: detalle, recargar: e.status === 409 };
}

function etiquetaPeriodo(p: Pick<PeriodoContable, 'numero' | 'nombre'>): string {
  return `${p.numero} · ${p.nombre}`;
}

/**
 * Quién y cuándo dejó el periodo en su estado actual (86e3t97vh): cerrado → último cierre; abierto → la reapertura si
 * la hubo, si no la apertura. La historia completa está en la bitácora.
 */
function responsable(p: PeriodoContable): string[] {
  if (p.estado === 'Cerrado' && p.cerradoPor) return [`Cerró ${p.cerradoPor} · ${formatDateTime(p.cerradoEn!)}`];
  if (p.estado === 'Abierto' && p.reabiertoPor) return [`Reabrió ${p.reabiertoPor} · ${formatDateTime(p.reabiertoEn!)}`];
  if (p.abiertoPor) return [`Abrió ${p.abiertoPor} · ${formatDateTime(p.abiertoEn!)}`];
  return [];
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
  const [transicion, setTransicion] = useState<{
    periodo: PeriodoContable;
    accion: 'cerrar' | 'reabrir';
  } | null>(null);
  const [bitacoraDe, setBitacoraDe] = useState<string | null>(null);
  const botonBitacora = useRef<HTMLButtonElement | null>(null);

  const lista = ejercicios.data ?? [];
  const ejercicio = lista.find((e) => e.anio === anioElegido) ?? lista[0];
  const periodos = ejercicio?.periodos ?? [];
  const [estadoVista, setEstadoVista] = useState<EstadoPeriodo>();
  const filtrados = periodos.filter((p) => !estadoVista || p.estado === estadoVista);
  const pagina = usePaginacionLocal(filtrados, String(ejercicio?.anio) + estadoVista);
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
    return p.numero === 13 && periodo12?.estado !== 'Cerrado'
      ? 'El periodo 13 se abre después de cerrar diciembre'
      : undefined;
  }

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <CalendarRange
              className="mr-2 inline-block size-5 text-brand"
              strokeWidth={1.6}
              aria-hidden="true"
            />
            Periodos contables
          </>
        }
        descripcion={
          <>
            Apertura, cierre y reapertura de los periodos del ejercicio, con motivo y bitácora de
            cada cambio.
          </>
        }
        acciones={
          <>
            {puedeAdministrar && (
              <Button className="ml-auto" onClick={() => setNuevo(true)} data-print="hidden">
                <Plus className="mr-1 size-4" aria-hidden="true" />
                Nuevo ejercicio
              </Button>
            )}
          </>
        }
      />

      {ejercicio && (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          {(Object.keys(ESTADOS_PERIODO) as EstadoPeriodo[]).map((estado) => (
            <button
              key={estado}
              type="button"
              className="text-left rounded-lg focus-visible:outline-2 focus-visible:outline-brand"
              onClick={() => setEstadoVista(estado)}
              aria-label={`Ver periodos: ${ESTADOS_PERIODO[estado].texto}`}
            >
              <KpiCard
                etiqueta={ESTADOS_PERIODO[estado].texto}
                valor={periodos.filter((p) => p.estado === estado).length}
                tono={ESTADOS_PERIODO[estado].tono}
                subdato={`Ejercicio ${ejercicio.anio}`}
              />
            </button>
          ))}
        </div>
      )}
      {ejercicios.isLoading ? (
        <div className="space-y-2" aria-label="Cargando ejercicios">
          <Skeleton className="h-8 w-60" />
          <Skeleton className="h-64 w-full" />
        </div>
      ) : ejercicios.isError ? (
        <div role="alert" className="flex items-center gap-2 text-sm text-danger-fg">
          No se pudieron cargar los ejercicios.
          <Button variant="ghost" size="sm" onClick={() => void ejercicios.refetch()}>
            Reintentar
          </Button>
        </div>
      ) : !ejercicio ? (
        <p className="rounded-lg bg-surface-card p-6 text-sm text-ink-muted shadow-card-flat">
          No hay ejercicios contables.{' '}
          {puedeAdministrar
            ? 'Crea el ejercicio del año para generar sus 13 periodos.'
            : 'Pide a Contabilidad que cree el ejercicio del año.'}
        </p>
      ) : (
        <>
          <ListadoModulo
            tabs={
              <TabsContabilidad
                estados={Object.entries(ESTADOS_PERIODO).map(([estado, ui]) => ({
                  estado: estado as EstadoPeriodo,
                  label: ui.texto,
                }))}
                valor={estadoVista}
                onChange={setEstadoVista}
              />
            }
            filtros={
              <BarraFiltros
                filtros={
                  <>
                    <div className="flex flex-col gap-1.5">
                      <Label htmlFor="f-ejercicio">Ejercicio</Label>
                      <select
                        id="f-ejercicio"
                        className={SELECT_CLASS}
                        value={ejercicio.anio}
                        onChange={(e) => elegirAnio(Number(e.target.value))}
                      >
                        {lista.map((e) => (
                          <option key={e.id} value={e.anio}>
                            {e.anio}
                          </option>
                        ))}
                      </select>
                    </div>
                    {puedeAdministrar && (
                      <Button
                        variant="outline"
                        className="ml-auto"
                        disabled={seleccion.length === 0}
                        title={
                          seleccion.length === 0
                            ? 'Marca los periodos sin abrir que quieras abrir'
                            : undefined
                        }
                        onClick={() => setAbriendo([...seleccion].sort((a, b) => a - b))}
                      >
                        Abrir seleccionados{seleccion.length > 0 ? ` (${seleccion.length})` : ''}
                      </Button>
                    )}
                  </>
                }
              />
            }
            pie={
              <PaginacionTabla
                total={filtrados.length}
                offset={pagina.offset}
                limit={10}
                tamanos={[]}
                onChange={pagina.onChange}
              />
            }
          >
            <div className="overflow-x-auto">
              <Table className="w-full min-w-220 text-sm">
                <TableHeader className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
                  <TableRow>
                    {puedeAdministrar && (
                      <TableHead className="w-9 px-3 py-2">
                        <span className="sr-only">Seleccionar</span>
                      </TableHead>
                    )}
                    <TableHead className="px-3 py-2">Periodo</TableHead>
                    <TableHead className="px-3 py-2">Fechas</TableHead>
                    <TableHead className="px-3 py-2">Estado</TableHead>
                    <TableHead className="px-3 py-2">Responsable</TableHead>
                    <TableHead className="px-3 py-2">
                      <span className="sr-only">Acciones</span>
                    </TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {pagina.visibles.map((p) => {
                    const bloqueo = bloqueoApertura(p);
                    return (
                      <TableRow
                        key={p.id}
                        className={`border-b border-line-row ${p.id === bitacoraDe ? 'bg-surface-row-focus' : ''}`}
                      >
                        {puedeAdministrar && (
                          <TableCell className="px-3 py-2">
                            {p.estado === 'NoAbierto' && !bloqueo && (
                              <Checkbox
                                aria-label={`Seleccionar ${etiquetaPeriodo(p)}`}
                                checked={seleccion.includes(p.numero)}
                                onCheckedChange={(v) => alternar(p.numero, v === true)}
                              />
                            )}
                          </TableCell>
                        )}
                        <TableCell className="px-3 py-2 font-medium">
                          {etiquetaPeriodo(p)}
                        </TableCell>
                        <TableCell className="px-3 py-2 text-ink-secondary">
                          {p.fechaInicio === p.fechaFin
                            ? fechaCorta(p.fechaInicio)
                            : `${fechaCorta(p.fechaInicio)} – ${fechaCorta(p.fechaFin)}`}
                        </TableCell>
                        <TableCell className="px-3 py-2">
                          <Badge variant={ESTADOS_PERIODO[p.estado].tono}>
                            {ESTADOS_PERIODO[p.estado].texto}
                          </Badge>
                        </TableCell>
                        <TableCell className="px-3 py-2 text-xs text-ink-muted">
                          {responsable(p).length === 0 && <span>Sin responsable</span>}
                          {responsable(p).map((l) => (
                            <span key={l} className="block">
                              {l}
                            </span>
                          ))}
                        </TableCell>
                        <TableCell className="px-3 py-2 text-right">
                          <span className="flex justify-end gap-1">
                            {puedeAdministrar && p.estado === 'NoAbierto' && (
                              <Button
                                variant="outline"
                                size="sm"
                                disabled={!!bloqueo}
                                title={bloqueo}
                                onClick={() => setAbriendo([p.numero])}
                              >
                                Abrir
                              </Button>
                            )}
                            {puedeCerrar && p.estado === 'Abierto' && (
                              <Button
                                variant="outline"
                                size="sm"
                                onClick={() => setTransicion({ periodo: p, accion: 'cerrar' })}
                              >
                                Cerrar
                              </Button>
                            )}
                            {puedeReabrir && p.estado === 'Cerrado' && (
                              <Button
                                variant="secondary-danger"
                                size="sm"
                                onClick={() => setTransicion({ periodo: p, accion: 'reabrir' })}
                              >
                                Reabrir
                              </Button>
                            )}
                            <Button
                              variant="ghost"
                              size="sm"
                              aria-haspopup="dialog"
                              aria-expanded={p.id === bitacoraDe}
                              aria-label={`Bitácora de ${etiquetaPeriodo(p)}`}
                              onClick={(event) => {
                                botonBitacora.current = event.currentTarget;
                                setBitacoraDe(p.id);
                              }}
                            >
                              <History className="size-3.5" strokeWidth={1.8} aria-hidden="true" />
                              Bitácora
                            </Button>
                          </span>
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </div>
            {!hayAcciones && (
              <p className="border-t border-line-divider px-4 py-2.5 text-xs text-ink-muted">
                Solo consulta: no tienes permisos para abrir, cerrar ni reabrir periodos.
              </p>
            )}
          </ListadoModulo>

          {conBitacora && (
            <BitacoraPanel
              periodo={conBitacora}
              onClose={() => setBitacoraDe(null)}
              devolverFoco={() => botonBitacora.current?.focus()}
            />
          )}
        </>
      )}

      {puedeAdministrar && (
        <NuevoEjercicioDialog
          open={nuevo}
          sugerido={
            lista.length ? Math.max(...lista.map((e) => e.anio)) + 1 : new Date().getFullYear()
          }
          onClose={(anio) => {
            setNuevo(false);
            if (anio) elegirAnio(anio);
          }}
        />
      )}
      {puedeAdministrar && ejercicio && (
        <AbrirDialog
          ejercicio={ejercicio}
          numeros={abriendo}
          onClose={(ok) => {
            setAbriendo(null);
            if (ok) setSeleccion([]);
          }}
        />
      )}
      <MotivoDialog transicion={transicion} onClose={() => setTransicion(null)} />
    </div>
  );
}

/** Error de una acción; con «Recargar» se vuelven a pedir los periodos y se cierra el diálogo. */
function ErrorAccion({
  error,
  onRecargar,
}: {
  error: { mensaje: string; recargar: boolean } | null;
  onRecargar: () => void;
}) {
  const qc = useQueryClient();
  if (!error) return null;
  return (
    <div role="alert" className="flex flex-wrap items-center gap-2 text-sm text-danger-fg">
      {error.mensaje}
      {error.recargar && (
        <Button
          variant="ghost"
          size="sm"
          onClick={() => {
            void qc.invalidateQueries({ queryKey: periodoKeys.all });
            onRecargar();
          }}
        >
          Recargar
        </Button>
      )}
    </div>
  );
}

function NuevoEjercicioDialog({
  open,
  sugerido,
  onClose,
}: {
  open: boolean;
  sugerido: number;
  onClose: (anio?: number) => void;
}) {
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
    crear.mutate(
      { ...command, idempotencyKey: keyFor(command) },
      { onSuccess: (e) => cerrar(e.anio), onError: (e) => setError(mensajeErrorPeriodo(e)) },
    );
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(o) => {
        if (!o) cerrar();
      }}
    >
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>Nuevo ejercicio</DialogTitle>
          <DialogDescription>
            Se generan los 12 periodos mensuales y el 13 de ajustes de auditoría, todos sin abrir.
            Después abre los que vayan a operarse.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="nuevo-anio">Año</Label>
            <Input
              id="nuevo-anio"
              type="number"
              min={2000}
              max={2999}
              className="w-32"
              value={valor}
              onChange={(e) => setAnio(e.target.value)}
            />
          </div>
          <ErrorAccion error={error} onRecargar={() => cerrar()} />
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={() => cerrar()}>
            Cancelar
          </Button>
          <Button onClick={confirmar} disabled={crear.isPending || !/^\d{4}$/.test(valor)}>
            {crear.isPending ? 'Creando…' : 'Crear ejercicio'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function AbrirDialog({
  ejercicio,
  numeros,
  onClose,
}: {
  ejercicio: EjercicioContable;
  numeros: number[] | null;
  onClose: (ok: boolean) => void;
}) {
  const abrir = useAbrirPeriodos();
  const keyFor = useBodyScopedIdempotencyKey();
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<{ mensaje: string; recargar: boolean } | null>(null);
  const nombres = (numeros ?? [])
    .map((n) => ejercicio.periodos.find((p) => p.numero === n))
    .filter((p) => !!p)
    .map(etiquetaPeriodo);

  function cerrar(ok: boolean) {
    setMotivo('');
    setError(null);
    onClose(ok);
  }

  function confirmar() {
    if (!numeros) return;
    setError(null);
    const command = {
      ejercicioId: ejercicio.id,
      version: ejercicio.version,
      numeros,
      motivo: motivo.trim() || null,
    };
    abrir.mutate(
      { ejercicio, numeros, motivo, idempotencyKey: keyFor(command) },
      { onSuccess: () => cerrar(true), onError: (e) => setError(mensajeErrorPeriodo(e)) },
    );
  }

  return (
    <Dialog
      open={!!numeros}
      onOpenChange={(o) => {
        if (!o) cerrar(false);
      }}
    >
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>
            {nombres.length > 1 ? `Abrir ${nombres.length} periodos` : 'Abrir periodo'}
          </DialogTitle>
          <DialogDescription>
            Ejercicio {ejercicio.anio}: {nombres.join(', ')}. Al abrirse admiten movimientos con
            fecha dentro del periodo.
            {nombres.length > 1 && ' Si alguno no puede abrirse, no se abre ninguno.'}
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="abrir-motivo">
              Motivo <span className="font-normal text-ink-muted">(opcional)</span>
            </Label>
            <Textarea
              id="abrir-motivo"
              maxLength={MOTIVO_MAXIMO}
              value={motivo}
              onChange={(e) => setMotivo(e.target.value)}
            />
          </div>
          <ErrorAccion error={error} onRecargar={() => cerrar(false)} />
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={() => cerrar(false)}>
            Cancelar
          </Button>
          <Button onClick={confirmar} disabled={abrir.isPending}>
            {abrir.isPending
              ? 'Abriendo…'
              : nombres.length > 1
                ? 'Abrir periodos'
                : 'Abrir periodo'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** Cerrar y reabrir: motivo obligatorio de 10 a 500 caracteres (D7), queda en la bitácora. */
function MotivoDialog({
  transicion,
  onClose,
}: {
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
  const nombre = transicion
    ? `${etiquetaPeriodo(transicion.periodo)} ${transicion.periodo.anio}`
    : '';

  function cerrar() {
    setMotivo('');
    setError(null);
    onClose();
  }

  function confirmar() {
    if (!transicion || !motivoValido) return;
    setError(null);
    const command = {
      periodoId: transicion.periodo.id,
      version: transicion.periodo.version,
      accion: transicion.accion,
      motivo: motivo.trim(),
    };
    mutacion.mutate(
      { ...transicion, motivo, idempotencyKey: keyFor(command) },
      { onSuccess: cerrar, onError: (e) => setError(mensajeErrorPeriodo(e)) },
    );
  }

  return (
    <Dialog
      open={!!transicion}
      onOpenChange={(o) => {
        if (!o) cerrar();
      }}
    >
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>{reabrir ? `Reabrir ${nombre}` : `Cerrar ${nombre}`}</DialogTitle>
          <DialogDescription>
            {reabrir
              ? 'El periodo vuelve a admitir movimientos. Solo se reabre si el periodo siguiente no está cerrado.'
              : 'El periodo deja de admitir movimientos. Los periodos se cierran en orden: los anteriores deben estar cerrados.'}
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          {reabrir && (
            <div
              role="note"
              className="flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg"
            >
              <TriangleAlert
                className="mt-0.5 size-4 shrink-0"
                strokeWidth={1.6}
                aria-hidden="true"
              />
              Reabrir la contabilidad no reabre el inventario: el cierre de Almacén se mantiene.
            </div>
          )}
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="transicion-motivo">Motivo</Label>
            <Textarea
              id="transicion-motivo"
              rows={3}
              maxLength={MOTIVO_MAXIMO}
              value={motivo}
              aria-describedby="transicion-motivo-ayuda"
              aria-invalid={largo > 0 && !motivoValido}
              onChange={(e) => setMotivo(e.target.value)}
            />
            <p id="transicion-motivo-ayuda" className="text-xs text-ink-muted">
              Obligatorio, de {MOTIVO_MINIMO} a {MOTIVO_MAXIMO} caracteres ({largo}). Queda en la
              bitácora del periodo.
            </p>
          </div>
          <ErrorAccion error={error} onRecargar={cerrar} />
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={cerrar}>
            Cancelar
          </Button>
          <Button
            variant={reabrir ? 'secondary-danger' : 'default'}
            onClick={confirmar}
            disabled={!motivoValido || mutacion.isPending}
            title={
              motivoValido ? undefined : `Escribe un motivo de al menos ${MOTIVO_MINIMO} caracteres`
            }
          >
            {mutacion.isPending ? 'Guardando…' : reabrir ? 'Reabrir periodo' : 'Cerrar periodo'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function BitacoraPanel({
  periodo,
  onClose,
  devolverFoco,
}: {
  periodo: PeriodoContable;
  onClose: () => void;
  devolverFoco: () => void;
}) {
  const bitacora = useBitacoraPeriodo(periodo.id);
  const cambios = [...(bitacora.data ?? [])].sort(
    (a, b) => b.versionResultante - a.versionResultante,
  );
  return (
    <Sheet
      open
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
    >
      <SheetContent
        size="lg"
        overlayClassName="bg-(--mt-scrim-sheet)"
        onCloseAutoFocus={(event) => {
          event.preventDefault();
          devolverFoco();
        }}
      >
        <SheetHeader className="gap-1 border-line-divider pr-16">
          <SheetTitle className="flex items-center gap-2 text-xl text-ink">
            <History
              className="mr-2 inline-block size-5 text-brand"
              strokeWidth={1.6}
              aria-hidden="true"
            />
            Bitácora del periodo
          </SheetTitle>
          <SheetDescription className="text-xs text-ink-muted">
            {periodo.nombre} · Ejercicio {periodo.anio}
          </SheetDescription>
        </SheetHeader>
        <SheetBody className="space-y-4">
          <div className="border-b border-line-divider bg-surface-subtle p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="text-sm font-medium">Estado actual</span>
              <Badge variant={ESTADOS_PERIODO[periodo.estado].tono}>
                {ESTADOS_PERIODO[periodo.estado].texto}
              </Badge>
            </div>
            <p className="mt-2 text-xs text-ink-muted">
              {periodo.fechaInicio === periodo.fechaFin
                ? fechaCorta(periodo.fechaInicio)
                : `${fechaCorta(periodo.fechaInicio)} – ${fechaCorta(periodo.fechaFin)}`}
            </p>
          </div>
          <div className="min-h-0">
            {bitacora.isLoading ? (
              <div role="status" aria-label="Cargando historial" className="space-y-4">
                <Skeleton className="h-8 w-60 max-w-full" />
                <Skeleton className="h-32 w-full" />
                <Skeleton className="h-32 w-full" />
              </div>
            ) : bitacora.isError ? (
              <div role="alert" className="flex flex-col items-start gap-3 text-sm">
                <p className="text-danger-fg">No se pudo cargar la bitácora.</p>
                <Button variant="outline" onClick={() => void bitacora.refetch()}>
                  Reintentar
                </Button>
              </div>
            ) : cambios.length === 0 ? (
              <div className="flex flex-col items-center gap-2 py-8 text-center">
                <History className="size-5 text-ink-muted" strokeWidth={1.6} aria-hidden="true" />
                <p className="text-sm font-medium">Sin cambios registrados</p>
                <p className="max-w-80 text-sm text-ink-muted">
                  Aquí aparecerán las aperturas, cierres y reaperturas de este periodo.
                </p>
              </div>
            ) : (
              <>
                <div className="mb-5 flex flex-wrap items-center justify-between gap-2 text-xs text-ink-muted">
                  <span>
                    {cambios.length}{' '}
                    {cambios.length === 1 ? 'cambio registrado' : 'cambios registrados'}
                  </span>
                  <span>Más reciente primero</span>
                </div>
                <ol aria-label="Cambios del periodo" className="space-y-5">
                  {cambios.map((b, indice) => (
                    <li key={b.id} className="relative border-l border-line-divider pl-5">
                      <span
                        className="absolute -left-1 top-1.5 size-2 rounded-full bg-ink-muted"
                        aria-hidden="true"
                      />
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <h3 className="text-sm font-semibold">{ETIQUETA_ACCION[b.accion]}</h3>
                        {indice === 0 && <Badge variant="neutral">Último cambio</Badge>}
                      </div>
                      <time dateTime={b.ocurridoEn} className="mt-1 block text-xs text-ink-muted">
                        {formatDateTime(b.ocurridoEn)}
                      </time>
                      <p className="mt-2 break-words text-sm text-ink-secondary">
                        Por <span className="font-medium text-ink">{b.usuarioNombre}</span>
                      </p>
                      <div
                        className="mt-3 flex flex-wrap items-center gap-2 text-xs"
                        aria-label={`${ESTADOS_PERIODO[b.estadoAnterior].texto} a ${ESTADOS_PERIODO[b.estadoNuevo].texto}`}
                      >
                        <Badge variant={ESTADOS_PERIODO[b.estadoAnterior].tono}>
                          {ESTADOS_PERIODO[b.estadoAnterior].texto}
                        </Badge>
                        <ArrowRight
                          className="size-3.5 text-ink-muted"
                          strokeWidth={1.8}
                          aria-hidden="true"
                        />
                        <Badge variant={ESTADOS_PERIODO[b.estadoNuevo].tono}>
                          {ESTADOS_PERIODO[b.estadoNuevo].texto}
                        </Badge>
                      </div>
                      {b.motivo ? (
                        <div className="mt-3 rounded-md bg-surface-subtle p-3">
                          <p className="mb-1 text-xs font-medium text-ink-muted">Motivo</p>
                          <p className="whitespace-pre-wrap break-words text-sm text-ink-secondary">
                            {b.motivo}
                          </p>
                        </div>
                      ) : (
                        <p className="mt-3 text-xs text-ink-muted">Sin motivo registrado.</p>
                      )}
                    </li>
                  ))}
                </ol>
              </>
            )}
          </div>
        </SheetBody>
        <SheetFooter className="border-line-divider bg-surface-subtle">
          <SheetClose asChild>
            <Button variant="outline" size="lg">
              Volver a periodos
            </Button>
          </SheetClose>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}
