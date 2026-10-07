import { useState } from 'react';
import { History, Lock } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { Textarea } from '@/components/ui/textarea';
import { formatDateTime } from '@/lib/datetime';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  useAccionPeriodo,
  useCrearEjercicio,
  useEjercicios,
  useHistorialPeriodo,
  usePeriodos,
  type AccionPeriodo,
  type Periodo,
} from '../api/periodos';
import { mensajeError } from '../lib/dimensiones';
import { SELECT_CLASS } from '../lib/estilos';

const MESES = ['Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio', 'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre'];
const MAX_MOTIVO = 500;

const ETIQUETA_ACCION: Record<AccionPeriodo, string> = { Creado: 'Creado', Cerrado: 'Cerrado', Reabierto: 'Reabierto' };

/** DateOnly del API (yyyy-MM-dd) → dd/MM/yyyy, sin pasar por zona horaria. */
function fechaCorta(d: string | null): string {
  if (!d) return '';
  const [a, m, dia] = d.split('-');
  return `${dia}/${m}/${a}`;
}

function nombrePeriodo(p: Periodo): string {
  return p.esPeriodoAjustes ? 'Periodo 13 · ajustes' : `${String(p.numero).padStart(2, '0')} · ${MESES[p.numero - 1]}`;
}

interface AccionPendiente {
  periodo: Periodo;
  accion: 'cerrar' | 'reabrir';
}

/**
 * F1-CON-03 (C1.1): los 13 periodos de cada ejercicio. Cerrar y reabrir quedan en el historial (quién, cuándo, motivo).
 * Reabrir es del Contador General (R18). Un ejercicio que no se ha creado no está abierto para registrar.
 */
export function PeriodosPage() {
  const puedeAdministrar = useHasPermission(PermisosCanonicos.ContabilidadPeriodoAdministrar);
  const puedeCerrar = useHasPermission(PermisosCanonicos.ContabilidadPeriodoCerrar);
  const puedeReabrir = useHasPermission(PermisosCanonicos.ContabilidadPeriodoReabrir);

  const ejercicios = useEjercicios();
  const [elegido, setElegido] = useState<number | null>(null);
  const ejercicio = elegido ?? ejercicios.data?.[0] ?? null;
  const periodos = usePeriodos(ejercicio);
  const [historialDe, setHistorialDe] = useState<Periodo | null>(null);
  const [pendiente, setPendiente] = useState<AccionPendiente | null>(null);

  return (
    <div className="flex flex-col gap-4 px-6 py-5">
      <header>
        <h1 className="flex items-center gap-2 text-3xl font-semibold">
          <Lock className="size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
          Periodos contables
        </h1>
        <p className="text-sm text-ink-muted">
          Doce meses y un periodo 13 de ajustes de auditoría por ejercicio. Cerrar impide registrar en el mes; reabrir lo hace el Contador General con motivo.
        </p>
      </header>

      <div className="flex flex-wrap items-end gap-3" data-print="hidden">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="periodos-ejercicio">Ejercicio</Label>
          <select
            id="periodos-ejercicio"
            className={`${SELECT_CLASS} w-32`}
            value={ejercicio ?? ''}
            disabled={!ejercicios.data?.length}
            onChange={(e) => { setElegido(Number(e.target.value)); setHistorialDe(null); }}
          >
            {ejercicios.data?.length ? ejercicios.data.map((a) => <option key={a} value={a}>{a}</option>) : <option value="">Sin ejercicios</option>}
          </select>
        </div>
        {puedeAdministrar && <CrearEjercicio onCreado={(a) => { setElegido(a); setHistorialDe(null); }} />}
      </div>

      <div className="overflow-hidden rounded-lg bg-surface-card shadow-card-flat">
        {ejercicios.isLoading || (ejercicio !== null && periodos.isLoading) ? (
          <div className="p-4" aria-label="Cargando periodos"><Skeleton className="h-8 w-full" /></div>
        ) : ejercicios.isError || periodos.isError ? (
          <p role="alert" className="p-4 text-sm text-danger-fg">No se pudieron cargar los periodos.</p>
        ) : ejercicio === null ? (
          <p className="p-6 text-sm text-ink-muted">
            Aún no hay ejercicios. Mientras un ejercicio no se crea, sus meses no están abiertos para registrar.
            {puedeAdministrar ? ' Créalo con «Crear ejercicio».' : ' Pide a quien administra los periodos que lo cree.'}
          </p>
        ) : (
          <table className="w-full text-sm">
            <thead className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
              <tr>
                <th className="px-3 py-2">Periodo</th>
                <th className="px-3 py-2">Fechas</th>
                <th className="px-3 py-2">Estado</th>
                <th className="px-3 py-2">Último cierre</th>
                <th className="px-3 py-2">Última reapertura</th>
                <th className="px-3 py-2"><span className="sr-only">Acciones</span></th>
              </tr>
            </thead>
            <tbody>
              {periodos.data!.map((p) => (
                <tr key={p.id} className={`border-b border-line-row ${historialDe?.id === p.id ? 'bg-surface-subtle' : ''}`}>
                  <td className="px-3 py-2 font-medium">{nombrePeriodo(p)}</td>
                  <td className="px-3 py-2 tabular-nums text-ink-muted">
                    {p.esPeriodoAjustes ? 'Después de cerrar diciembre' : `${fechaCorta(p.fechaInicio)} – ${fechaCorta(p.fechaFin)}`}
                  </td>
                  <td className="px-3 py-2">
                    <Badge variant={p.estado === 'Abierto' ? 'success' : 'neutral'}>{p.estado}</Badge>
                  </td>
                  <td className="px-3 py-2 text-ink-muted">{p.cerradoPor ? `${p.cerradoPor} · ${formatDateTime(p.cerradoEn!)}` : '—'}</td>
                  <td className="px-3 py-2 text-ink-muted">{p.reabiertoPor ? `${p.reabiertoPor} · ${formatDateTime(p.reabiertoEn!)}` : '—'}</td>
                  <td className="px-3 py-2">
                    <span className="flex justify-end gap-2" data-print="hidden">
                      {p.estado === 'Abierto' && puedeCerrar && (
                        <Button variant="outline" size="sm" onClick={() => setPendiente({ periodo: p, accion: 'cerrar' })} aria-label={`Cerrar ${nombrePeriodo(p)}`}>
                          Cerrar
                        </Button>
                      )}
                      {p.estado === 'Cerrado' && puedeReabrir && (
                        <Button variant="outline" size="sm" onClick={() => setPendiente({ periodo: p, accion: 'reabrir' })} aria-label={`Reabrir ${nombrePeriodo(p)}`}>
                          Reabrir
                        </Button>
                      )}
                      <Button variant="ghost" size="sm" onClick={() => setHistorialDe(p)} aria-label={`Historial de ${nombrePeriodo(p)}`}>
                        <History className="size-4" aria-hidden="true" />
                      </Button>
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {historialDe && <HistorialPanel periodo={historialDe} onCerrar={() => setHistorialDe(null)} />}

      <MotivoDialog pendiente={pendiente} onCerrar={() => setPendiente(null)} />
    </div>
  );
}

function CrearEjercicio({ onCreado }: { onCreado: (ejercicio: number) => void }) {
  const crear = useCrearEjercicio();
  const [anio, setAnio] = useState(String(new Date().getFullYear()));
  const [error, setError] = useState('');

  function enviar(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    crear.mutate(Number(anio), { onSuccess: (r) => onCreado(r.ejercicio), onError: (x) => setError(mensajeError(x)) });
  }

  return (
    <form onSubmit={enviar} className="flex flex-wrap items-end gap-3">
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="periodos-nuevo">Nuevo ejercicio</Label>
        <Input id="periodos-nuevo" type="number" required min={2000} max={2100} className="w-28 tabular-nums" value={anio} onChange={(e) => setAnio(e.target.value)} />
      </div>
      <Button type="submit" disabled={crear.isPending}>{crear.isPending ? 'Creando…' : 'Crear ejercicio'}</Button>
      {error && <p role="alert" className="basis-full text-sm text-danger-fg">{error}</p>}
    </form>
  );
}

function HistorialPanel({ periodo, onCerrar }: { periodo: Periodo; onCerrar: () => void }) {
  const historial = useHistorialPeriodo(periodo.id);
  return (
    <section aria-label={`Historial de ${nombrePeriodo(periodo)}`} className="rounded-lg bg-surface-card p-4 shadow-card-flat">
      <div className="mb-3 flex items-center gap-2">
        <h2 className="text-base font-semibold">Historial · {nombrePeriodo(periodo)} {periodo.ejercicio}</h2>
        <Button variant="ghost" size="sm" className="ml-auto" onClick={onCerrar} data-print="hidden">Ocultar</Button>
      </div>
      {historial.isLoading ? (
        <Skeleton className="h-8 w-full" />
      ) : historial.isError ? (
        <p role="alert" className="text-sm text-danger-fg">No se pudo cargar el historial.</p>
      ) : (
        <ol className="flex flex-col gap-2 text-sm">
          {historial.data!.map((ev) => (
            <li key={ev.id} className="flex flex-wrap items-baseline gap-x-2 border-b border-line-row pb-2 last:border-0">
              <Badge variant={ev.accion === 'Reabierto' ? 'warning' : ev.accion === 'Cerrado' ? 'neutral' : 'info'}>{ETIQUETA_ACCION[ev.accion]}</Badge>
              <span>{ev.usuario}</span>
              <span className="tabular-nums text-ink-muted">{formatDateTime(ev.fecha)}</span>
              {ev.motivo && <span className="basis-full text-ink-muted">Motivo: {ev.motivo}</span>}
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}

function MotivoDialog({ pendiente, onCerrar }: { pendiente: AccionPendiente | null; onCerrar: () => void }) {
  const accion = useAccionPeriodo();
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState('');
  const reabrir = pendiente?.accion === 'reabrir';
  const falta = reabrir && motivo.trim().length === 0;

  function cerrar() {
    setMotivo('');
    setError('');
    accion.reset();
    onCerrar();
  }

  function confirmar() {
    if (!pendiente) return;
    setError('');
    accion.mutate(
      { periodo: pendiente.periodo, accion: pendiente.accion, motivo: motivo.trim() || null },
      { onSuccess: cerrar, onError: (x) => setError(mensajeError(x)) },
    );
  }

  return (
    <Dialog open={pendiente !== null} onOpenChange={(abierto) => { if (!abierto) cerrar(); }}>
      <DialogContent className="sm:max-w-md">
        {pendiente && (
          <>
            <DialogHeader>
              <DialogTitle>{reabrir ? 'Reabrir' : 'Cerrar'} {nombrePeriodo(pendiente.periodo)} {pendiente.periodo.ejercicio}</DialogTitle>
              <DialogDescription>
                {reabrir
                  ? 'Se podrá volver a registrar en el periodo. El cierre de inventario de Almacén no cambia. Queda en el historial con tu nombre y el motivo.'
                  : 'Ya no se podrá registrar en el periodo hasta que el Contador General lo reabra. Queda en el historial con tu nombre.'}
              </DialogDescription>
            </DialogHeader>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="periodo-motivo">{reabrir ? 'Motivo (obligatorio)' : 'Motivo (opcional)'}</Label>
              <Textarea id="periodo-motivo" rows={3} maxLength={MAX_MOTIVO} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
            </div>
            {error && <p role="alert" className="text-sm text-danger-fg">{error}</p>}
            <DialogFooter>
              <Button variant="ghost" onClick={cerrar} disabled={accion.isPending}>Cancelar</Button>
              <Button onClick={confirmar} disabled={accion.isPending || falta}>
                {accion.isPending ? 'Guardando…' : reabrir ? 'Reabrir periodo' : 'Cerrar periodo'}
              </Button>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}
