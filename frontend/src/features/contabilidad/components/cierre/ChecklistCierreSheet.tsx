import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { ClipboardCheck, ExternalLink } from 'lucide-react';
import { ErrorState } from '@/components/erp';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Label } from '@/components/ui/label';
import {
  Sheet,
  SheetBody,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Skeleton } from '@/components/ui/skeleton';
import { Textarea } from '@/components/ui/textarea';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { formatDateTime } from '@/lib/datetime';
import {
  MOTIVO_MARCA_MAXIMO,
  MOTIVO_MARCA_MINIMO,
  useCierreAnual,
  useCierreRegistrado,
  useChecklistCierre,
  useMarcarPaso,
  useRetirarMarca,
  type CierreRegistrado,
  type EstadoPasoChecklist,
  type PasoChecklist,
  type TipoMarcaPasoCierre,
} from '../../api/cierre';
import type { PeriodoContable } from '../../api/periodos';

type Tono = 'success' | 'warning' | 'danger' | 'info' | 'neutral';

export const ESTADOS_PASO: Record<EstadoPasoChecklist, { tono: Tono; texto: string }> = {
  Cumple: { tono: 'success', texto: 'Cumple' },
  Pendiente: { tono: 'warning', texto: 'Pendiente' },
  NoAplica: { tono: 'neutral', texto: 'No aplica' },
  Excepcion: { tono: 'info', texto: 'Excepción autorizada' },
  Confirmado: { tono: 'success', texto: 'Confirmado' },
};

function mensajeError(e: unknown): string {
  if (!esApiError(e)) return 'No se pudo completar la operación (sin conexión o error inesperado). Reintenta.';
  return e.problem.detail ?? e.problem.title ?? 'No se pudo completar la operación.';
}

/**
 * CON-12 (86e3jvvme) · checklist de cierre del periodo: qué pasos se cumplen, qué falta (con la pantalla donde se atiende),
 * excepciones autorizadas y confirmaciones de pasos manuales. Desde aquí se pasa a «Cerrar periodo» (el backend vuelve a
 * exigir el checklist, CA10.9). En el periodo 13 se genera el cierre anual.
 */
export function ChecklistCierreSheet({
  periodo,
  onClose,
  onCerrarPeriodo,
}: {
  periodo: PeriodoContable;
  onClose: () => void;
  onCerrarPeriodo: (p: PeriodoContable) => void;
}) {
  const checklist = useChecklistCierre(periodo.id);
  const puedeCerrar = useHasPermission(PermisosCanonicos.ContabilidadPeriodoCerrar);
  const puedeExcepcion = useHasPermission(PermisosCanonicos.ContabilidadCierreAutorizarExcepcion);
  const puedeAnual = useHasPermission(PermisosCanonicos.ContabilidadCierreAnual);
  const [marcando, setMarcando] = useState<{ paso: PasoChecklist; tipo: TipoMarcaPasoCierre } | null>(null);
  const retirar = useRetirarMarca();
  const [errorRetiro, setErrorRetiro] = useState<string | null>(null);
  const abierto = periodo.estado === 'Abierto';
  const datos = checklist.data;

  return (
    <Sheet
      open
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
    >
      <SheetContent size="lg" overlayClassName="bg-(--mt-scrim-sheet)">
        <SheetHeader className="gap-1 border-line-divider pr-16">
          <SheetTitle className="flex items-center gap-2 text-xl text-ink">
            <ClipboardCheck className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Checklist de cierre
          </SheetTitle>
          <SheetDescription className="text-xs text-ink-muted">
            {periodo.nombre} · Ejercicio {periodo.anio}
          </SheetDescription>
        </SheetHeader>
        <SheetBody className="space-y-4">
          {checklist.isLoading ? (
            <div role="status" aria-label="Verificando el checklist" className="space-y-3">
              <Skeleton className="h-8 w-60 max-w-full" />
              <Skeleton className="h-24 w-full" />
              <Skeleton className="h-24 w-full" />
            </div>
          ) : checklist.isError || !datos ? (
            <ErrorState
              title="No se pudo verificar el checklist"
              problem={esApiError(checklist.error) ? checklist.error.problem : undefined}
              onRetry={() => void checklist.refetch()}
            />
          ) : (
            <>
              <div className="border-b border-line-divider bg-surface-subtle p-4">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="text-sm font-medium">
                    {datos.bloqueantes === 0
                      ? abierto
                        ? 'Listo para cerrar'
                        : 'Sin pasos pendientes'
                      : `${datos.bloqueantes} ${datos.bloqueantes === 1 ? 'paso obligatorio pendiente' : 'pasos obligatorios pendientes'}`}
                  </span>
                  {datos.excepciones > 0 && (
                    <Badge variant="info">
                      {datos.excepciones} {datos.excepciones === 1 ? 'excepción' : 'excepciones'}
                    </Badge>
                  )}
                </div>
                <p className="mt-2 text-xs text-ink-muted">
                  Verificado {formatDateTime(datos.evaluadoEn)}.
                  {!datos.exigeChecklist && ' En este ambiente el cierre no exige el checklist.'}
                </p>
              </div>

              <ol aria-label="Pasos del checklist" className="space-y-3">
                {datos.pasos.map((paso) => (
                  <PasoItem
                    key={paso.clave}
                    paso={paso}
                    acciones={abierto}
                    puedeExcepcion={puedeExcepcion}
                    puedeConfirmar={puedeCerrar}
                    retirando={retirar.isPending}
                    onMarcar={(tipo) => setMarcando({ paso, tipo })}
                    onRetirar={(marcaId) => {
                      setErrorRetiro(null);
                      retirar.mutate(marcaId, { onError: (e) => setErrorRetiro(mensajeError(e)) });
                    }}
                  />
                ))}
              </ol>
              {errorRetiro && (
                <p role="alert" className="text-sm text-danger-fg">
                  {errorRetiro}
                </p>
              )}

              {periodo.esAjuste && abierto && puedeAnual && <CierreAnualAccion anio={periodo.anio} />}

              {datos.cierres.length > 0 && (
                <section aria-labelledby="cierres-registrados" className="space-y-2">
                  <h3 id="cierres-registrados" className="text-sm font-semibold">
                    Cierres registrados
                  </h3>
                  <ul className="space-y-2">
                    {datos.cierres.map((c) => (
                      <CierreRegistradoItem key={c.id} cierre={c} />
                    ))}
                  </ul>
                </section>
              )}
            </>
          )}
        </SheetBody>
        <SheetFooter className="border-line-divider bg-surface-subtle">
          <SheetClose asChild>
            <Button variant="outline" size="lg">
              Volver a periodos
            </Button>
          </SheetClose>
          {puedeCerrar && abierto && (
            <Button
              size="lg"
              disabled={!datos?.puedeCerrar}
              title={
                datos?.puedeCerrar
                  ? undefined
                  : 'Faltan pasos obligatorios: resuélvelos o pide una excepción autorizada'
              }
              onClick={() => onCerrarPeriodo(periodo)}
            >
              Cerrar periodo
            </Button>
          )}
        </SheetFooter>
      </SheetContent>
      {marcando && (
        <MarcaDialog
          periodoId={periodo.id}
          paso={marcando.paso}
          tipo={marcando.tipo}
          onClose={() => setMarcando(null)}
        />
      )}
    </Sheet>
  );
}

function PasoItem({
  paso,
  acciones,
  puedeExcepcion,
  puedeConfirmar,
  retirando,
  onMarcar,
  onRetirar,
}: {
  paso: PasoChecklist;
  acciones: boolean;
  puedeExcepcion: boolean;
  puedeConfirmar: boolean;
  retirando: boolean;
  onMarcar: (tipo: TipoMarcaPasoCierre) => void;
  onRetirar: (marcaId: string) => void;
}) {
  const estado = ESTADOS_PASO[paso.estado];
  const pendiente = paso.estado === 'Pendiente';
  return (
    <li className="rounded-md border border-line-divider p-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="text-sm font-semibold">{paso.nombre}</p>
          <p className="text-xs text-ink-muted">
            {paso.obligatorio ? 'Obligatorio' : 'Opcional'} · {paso.tipo === 'Manual' ? 'Se confirma a mano' : 'Lo verifica el ERP'}
          </p>
        </div>
        <Badge variant={estado.tono}>{estado.texto}</Badge>
      </div>
      <p className="mt-2 break-words text-sm text-ink-secondary">{paso.resumen}</p>
      {paso.bloquea && <p className="mt-1 text-xs font-medium text-warning-fg">Impide cerrar el periodo.</p>}
      {paso.detalle.length > 0 && (
        <ul aria-label={`Detalle de ${paso.nombre}`} className="mt-2 list-disc space-y-0.5 pl-5 text-xs text-ink-secondary">
          {paso.detalle.map((d) => (
            <li key={d} className="break-words">
              {d}
            </li>
          ))}
        </ul>
      )}
      {paso.marca && (
        <div className="mt-2 rounded-md bg-surface-subtle p-2.5 text-xs">
          <p className="text-ink-secondary">
            {paso.marca.tipo === 'Excepcion' ? 'Autorizó' : 'Confirmó'}{' '}
            <span className="font-medium text-ink">{paso.marca.usuarioNombre}</span> · {formatDateTime(paso.marca.registradaEn)}
          </p>
          <p className="mt-1 whitespace-pre-wrap break-words text-ink-secondary">{paso.marca.motivo}</p>
        </div>
      )}
      <div className="mt-2 flex flex-wrap items-center gap-2">
        {paso.ruta && (
          <Button asChild variant="ghost" size="sm">
            <Link to={paso.ruta as never}>
              <ExternalLink className="size-3.5" strokeWidth={1.8} aria-hidden="true" />
              Ir a la pantalla
            </Link>
          </Button>
        )}
        {acciones && pendiente && paso.tipo === 'Manual' && puedeConfirmar && (
          <Button variant="outline" size="sm" onClick={() => onMarcar('Confirmacion')}>
            Confirmar paso
          </Button>
        )}
        {acciones && pendiente && puedeExcepcion && (
          <Button variant="outline" size="sm" onClick={() => onMarcar('Excepcion')}>
            Autorizar excepción
          </Button>
        )}
        {acciones && paso.marca && (paso.marca.tipo === 'Confirmacion' ? puedeConfirmar : puedeExcepcion) && (
          <Button variant="ghost" size="sm" disabled={retirando} onClick={() => onRetirar(paso.marca!.id)}>
            {paso.marca.tipo === 'Excepcion' ? 'Retirar excepción' : 'Retirar confirmación'}
          </Button>
        )}
      </div>
    </li>
  );
}

function MarcaDialog({
  periodoId,
  paso,
  tipo,
  onClose,
}: {
  periodoId: string;
  paso: PasoChecklist;
  tipo: TipoMarcaPasoCierre;
  onClose: () => void;
}) {
  const marcar = useMarcarPaso();
  const keyFor = useBodyScopedIdempotencyKey();
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const largo = motivo.trim().length;
  const valido = largo >= MOTIVO_MARCA_MINIMO && largo <= MOTIVO_MARCA_MAXIMO;
  const excepcion = tipo === 'Excepcion';

  function confirmar() {
    if (!valido) return;
    setError(null);
    const command = { periodoId, clave: paso.clave, tipo, motivo: motivo.trim() };
    marcar.mutate(
      { ...command, idempotencyKey: keyFor(command) },
      { onSuccess: onClose, onError: (e) => setError(mensajeError(e)) },
    );
  }

  return (
    <Dialog
      open
      onOpenChange={(o) => {
        if (!o) onClose();
      }}
    >
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>{excepcion ? 'Autorizar excepción' : 'Confirmar paso'}</DialogTitle>
          <DialogDescription>
            {excepcion
              ? `El periodo se podrá cerrar con «${paso.nombre}» pendiente. Queda registrado con tu nombre, la fecha y el motivo.`
              : `Confirma que «${paso.nombre}» está hecho. Queda registrado con tu nombre y la nota.`}
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-2">
          <Label htmlFor="marca-motivo">{excepcion ? 'Motivo *' : 'Nota *'}</Label>
          <Textarea
            id="marca-motivo"
            rows={3}
            maxLength={MOTIVO_MARCA_MAXIMO}
            value={motivo}
            aria-describedby="marca-motivo-ayuda"
            aria-invalid={largo > 0 && !valido}
            onChange={(e) => setMotivo(e.target.value)}
          />
          <p id="marca-motivo-ayuda" className="text-xs text-ink-muted">
            De {MOTIVO_MARCA_MINIMO} a {MOTIVO_MARCA_MAXIMO} caracteres ({largo}).
          </p>
          {error && (
            <p role="alert" className="text-sm text-danger-fg">
              {error}
            </p>
          )}
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={onClose}>
            Cancelar
          </Button>
          <Button
            onClick={confirmar}
            disabled={!valido || marcar.isPending}
            title={valido ? undefined : `Escribe al menos ${MOTIVO_MARCA_MINIMO} caracteres`}
          >
            {marcar.isPending ? 'Guardando…' : excepcion ? 'Autorizar excepción' : 'Confirmar paso'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function CierreAnualAccion({ anio }: { anio: number }) {
  const anual = useCierreAnual();
  const keyFor = useBodyScopedIdempotencyKey();
  const [error, setError] = useState<string | null>(null);
  const r = anual.data;
  return (
    <section aria-labelledby="cierre-anual" className="space-y-2 rounded-md border border-line-divider p-3">
      <h3 id="cierre-anual" className="text-sm font-semibold">
        Cierre anual {anio}
      </h3>
      <p className="text-xs text-ink-muted">
        Genera en este periodo las pólizas en borrador que dejan en cero las cuentas de resultados contra resultado del
        ejercicio. Después se autorizan en Pólizas.
      </p>
      <Button
        variant="outline"
        size="sm"
        disabled={anual.isPending}
        onClick={() => {
          setError(null);
          anual.mutate({ anio, idempotencyKey: keyFor({ anio }) }, { onError: (e) => setError(mensajeError(e)) });
        }}
      >
        {anual.isPending ? 'Generando…' : 'Generar cierre anual'}
      </Button>
      {error && (
        <p role="alert" className="text-sm text-danger-fg">
          {error}
        </p>
      )}
      {r && (
        <p role="status" className="text-sm text-ink-secondary">
          {r.tipo} del ejercicio{' '}
          <span className="tabular-nums">
            {r.resultado.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' })}
          </span>{' '}
          · {r.cuentasConSaldo} cuentas · {r.polizas.length} {r.polizas.length === 1 ? 'póliza' : 'pólizas'} en borrador.
        </p>
      )}
    </section>
  );
}

/** Foto del checklist de un cierre: con qué excepciones se cerró y quién las autorizó (CON-12-a). */
function CierreRegistradoItem({ cierre }: { cierre: CierreRegistrado }) {
  const [ver, setVer] = useState(false);
  const foto = useCierreRegistrado(ver ? cierre.id : null);
  const excepciones = foto.data?.pasos.filter((p) => p.estado === 'Excepcion') ?? [];
  return (
    <li className="rounded-md bg-surface-subtle p-2.5 text-xs">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-ink-secondary">
          Cerró <span className="font-medium text-ink">{cierre.cerradoPor}</span> · {formatDateTime(cierre.cerradoEn)}
        </span>
        {cierre.excepciones > 0 ? (
          <Button variant="ghost" size="sm" aria-expanded={ver} onClick={() => setVer((v) => !v)}>
            {cierre.excepciones} {cierre.excepciones === 1 ? 'excepción' : 'excepciones'}
          </Button>
        ) : (
          <span className="text-ink-muted">Sin excepciones</span>
        )}
      </div>
      {ver && (
        <ul className="mt-2 space-y-1">
          {foto.isLoading && <li className="text-ink-muted">Cargando…</li>}
          {excepciones.map((p) => (
            <li key={p.clave} className="break-words text-ink-secondary">
              <span className="font-medium text-ink">{p.nombre}</span>: autorizó {p.marca?.usuarioNombre} ·{' '}
              {p.marca ? formatDateTime(p.marca.registradaEn) : ''} · {p.marca?.motivo}
            </li>
          ))}
        </ul>
      )}
    </li>
  );
}
