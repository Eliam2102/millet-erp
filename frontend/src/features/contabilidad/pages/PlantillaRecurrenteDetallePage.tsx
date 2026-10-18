import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { CheckCheck, Pause, Pencil, Play, Printer, RotateCcw, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { formatDateTime } from '@/lib/datetime';
import { cn } from '@/lib/utils';
import { useSucursalesMovimiento } from '../api/dimensiones';
import {
  useCambiarEstadoPlantilla, useEtiquetasCuentas, useGenerarPendientes, usePlantillaRecurrente, usePlantillasRecurrentes,
} from '../api/recurrentes';
import type {
  AccionPlantilla, AplicacionRecurrente, PartidaPlantilla, PlantillaRecurrente, ResultadoGeneracion as Resultado,
} from '../api/recurrentes-types';
import { EstadoPolizaBadge } from '../components/polizas/EstadoPolizaBadge';
import { EstadoPlantillaBadge } from '../components/recurrentes/EstadoPlantillaBadge';
import { PlantillaSheet } from '../components/recurrentes/PlantillaSheet';
import { ResultadoGeneracion } from '../components/recurrentes/ResultadoGeneracion';
import { fechaCorta } from '../lib/dimensiones';
import { folioOBorrador, importeLado, monto } from '../lib/polizas';
import { etiquetaDia, etiquetaMes, etiquetaVigencia, mensajeRecurrente, type RecurrentesSearch } from '../lib/recurrentes';

interface Props {
  id: string;
  search: RecurrentesSearch;
}

/** Detalle de plantilla recurrente (P3 master-detail, K10.4): datos, partidas fijas, calendario de aplicaciones y acciones. */
export function PlantillaRecurrenteDetallePage({ id, search }: Props) {
  return (
    <div className="flex min-h-0 flex-1">
      <ListaCompacta idActivo={id} search={search} />
      <div className="min-w-0 flex-1">
        <DetallePlantilla id={id} search={search} />
      </div>
    </div>
  );
}

function ListaCompacta({ idActivo, search }: { idActivo: string; search: RecurrentesSearch }) {
  const lista = usePlantillasRecurrentes(search.estado);
  const items = lista.data ?? [];
  return (
    <aside
      aria-label="Plantillas recurrentes"
      className="sticky top-0 hidden max-h-[calc(100vh-56px)] w-80 shrink-0 overflow-y-auto border-r border-line bg-surface-card lg:block"
      data-print="hidden"
    >
      <p className="border-b border-line-divider px-3.5 py-3 text-xs text-ink-muted">
        {lista.data ? `${items.length} ${items.length === 1 ? 'plantilla' : 'plantillas'}` : 'Cargando…'}
      </p>
      <ul>
        {items.map((p) => (
          <li key={p.id}>
            <Link
              to="/contabilidad/polizas/recurrentes/$id" params={{ id: p.id }} search={search}
              aria-current={p.id === idActivo ? 'page' : undefined}
              className={cn('block border-b border-line-row px-3.5 py-[11px] text-sm', p.id === idActivo && 'bg-surface-selected shadow-list-selected')}
            >
              <span className="flex items-center justify-between gap-2">
                <span className="truncate text-xs font-medium text-brand">{p.nombre}</span>
                <span className="shrink-0 text-xs font-medium tabular-nums">{monto(p.importe)}</span>
              </span>
              <span className="mt-0.5 flex items-center justify-between gap-2 text-xs text-ink-muted">
                <span>{p.estado} · {p.generadas} de {p.numeroPeriodos}</span>
                <span className="shrink-0 font-mono">{p.tipoClave}</span>
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </aside>
  );
}

const TEXTO_ACCION: Record<AccionPlantilla, { haciendo: string; hecho: string }> = {
  activar: { haciendo: 'Activando…', hecho: 'Plantilla activada. Se generaron las pólizas que ya tocaban.' },
  suspender: { haciendo: 'Suspendiendo…', hecho: 'Plantilla suspendida: no generará pólizas hasta reanudarla.' },
  reanudar: { haciendo: 'Reanudando…', hecho: 'Plantilla reanudada. Se generaron las pólizas pendientes.' },
};

function DetallePlantilla({ id, search }: Props) {
  const consulta = usePlantillaRecurrente(id);
  const p = consulta.data;
  const usuarioId = useAuthStore((s) => s.user?.id ?? null);
  const puedeAdministrar = useHasPermission(PermisosCanonicos.ContabilidadRecurrentesAdministrar);
  const puedeAutorizar = useHasPermission(PermisosCanonicos.ContabilidadRecurrentesAutorizar);
  const cambiar = useCambiarEstadoPlantilla();
  const generar = useGenerarPendientes();
  const keys: Record<AccionPlantilla, string> = {
    activar: useFormIdempotencyKey(),
    suspender: useFormIdempotencyKey(),
    reanudar: useFormIdempotencyKey(),
  };
  const keyGenerar = useFormIdempotencyKey();
  const [editando, setEditando] = useState(false);
  const [accionEnCurso, setAccionEnCurso] = useState<AccionPlantilla | null>(null);
  const [mensaje, setMensaje] = useState('');
  const [aviso, setAviso] = useState('');
  const [resultado, setResultado] = useState<Resultado | null>(null);

  const cerrar = (
    <Button asChild variant="ghost" size="icon" aria-label="Cerrar y volver a la bandeja">
      <Link to="/contabilidad/polizas/recurrentes" search={search}><X className="size-4" aria-hidden="true" /></Link>
    </Button>
  );

  if (consulta.isLoading) {
    return (
      <div className="space-y-3 px-4 py-5 sm:px-7" aria-label="Cargando plantilla">
        <Skeleton className="h-8 w-1/2" /><Skeleton className="h-32 w-full" />
      </div>
    );
  }
  if (consulta.isError || !p) {
    const noExiste = esApiError(consulta.error) && consulta.error.status === 404;
    return (
      <div className="space-y-3 px-4 py-5 sm:px-7">
        <div className="flex justify-end" data-print="hidden">{cerrar}</div>
        <div role="alert" className="flex items-center gap-2 rounded-lg bg-danger-bg p-4 text-sm text-danger-fg">
          {noExiste ? 'La plantilla no existe.' : `No se pudo cargar la plantilla. ${mensajeRecurrente(consulta.error)}`}
          {!noExiste && <Button variant="ghost" size="sm" onClick={() => void consulta.refetch()}>Reintentar</Button>}
        </div>
      </div>
    );
  }

  const preparoElUsuario = !!usuarioId && !!p.creadaPorId && usuarioId === p.creadaPorId;
  const motivoNoActiva = preparoElUsuario ? 'No puedes activar una plantilla que tú preparaste: la activa otro usuario.' : undefined;
  const pendiente = cambiar.isPending || generar.isPending;

  function onAccion(plantilla: PlantillaRecurrente, accion: AccionPlantilla) {
    setMensaje(''); setAviso(''); setResultado(null); setAccionEnCurso(accion);
    cambiar.mutate(
      { plantilla, accion, idempotencyKey: keys[accion] },
      {
        onSuccess: () => setAviso(TEXTO_ACCION[accion].hecho),
        onError: (e) => setMensaje(mensajeRecurrente(e)),
        onSettled: () => setAccionEnCurso(null),
      },
    );
  }

  function onGenerar(plantilla: PlantillaRecurrente) {
    setMensaje(''); setAviso(''); setResultado(null);
    generar.mutate(
      { plantillaId: plantilla.id, idempotencyKey: keyGenerar },
      { onSuccess: setResultado, onError: (e) => setMensaje(mensajeRecurrente(e)) },
    );
  }

  const textoBoton = (accion: AccionPlantilla, texto: string) => (accionEnCurso === accion ? TEXTO_ACCION[accion].haciendo : texto);

  return (
    <div className="flex flex-col">
      <div
        className="sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b border-line bg-surface-card px-4 py-2 sm:px-7"
        data-print="hidden"
      >
        <span className="max-w-[240px] truncate text-sm font-medium">{p.nombre}</span>
        <EstadoPlantillaBadge estado={p.estado} />
        <span className="ml-auto flex flex-wrap items-center gap-2">
          {p.estado === 'Borrador' && puedeAdministrar && (
            <Button variant="secondary" onClick={() => setEditando(true)}><Pencil className="mr-1 size-3.5" aria-hidden="true" />Editar</Button>
          )}
          {p.estado === 'Activa' && puedeAdministrar && (
            <>
              <Button variant="secondary" disabled={pendiente} onClick={() => onGenerar(p)}>
                <Play className="mr-1 size-3.5" aria-hidden="true" />{generar.isPending ? 'Generando…' : 'Generar pendientes'}
              </Button>
              <Button variant="secondary" disabled={pendiente} onClick={() => onAccion(p, 'suspender')}>
                <Pause className="mr-1 size-3.5" aria-hidden="true" />{textoBoton('suspender', 'Suspender')}
              </Button>
            </>
          )}
          {p.estado === 'Suspendida' && puedeAdministrar && (
            <Button disabled={pendiente} onClick={() => onAccion(p, 'reanudar')}>
              <RotateCcw className="mr-1 size-3.5" aria-hidden="true" />{textoBoton('reanudar', 'Reanudar')}
            </Button>
          )}
          {p.estado === 'Borrador' && puedeAutorizar && (
            <Button disabled={!!motivoNoActiva || pendiente} title={motivoNoActiva} onClick={() => onAccion(p, 'activar')}>
              <CheckCheck className="mr-1 size-3.5" aria-hidden="true" />{textoBoton('activar', 'Activar')}
            </Button>
          )}
          <Button variant="ghost" size="icon" aria-label="Imprimir plantilla" onClick={() => window.print()}>
            <Printer className="size-4" strokeWidth={1.6} aria-hidden="true" />
          </Button>
          {cerrar}
        </span>
      </div>

      <div className="flex flex-col gap-5 px-4 py-6 sm:px-7">
        <header className="flex flex-col gap-1">
          <h1 className="flex flex-wrap items-center gap-2 text-3xl font-semibold">
            <span>{p.nombre}</span>
            <EstadoPlantillaBadge estado={p.estado} />
          </h1>
          <p className="text-sm text-ink-secondary">
            Plantilla recurrente · <span className="font-mono text-xs">{p.tipoClave}</span> · {etiquetaVigencia(p.anioInicio, p.mesInicio, p.numeroPeriodos)}
          </p>
        </header>

        {p.estado === 'Borrador' && motivoNoActiva && puedeAutorizar && (
          <p role="note" className="rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg">{motivoNoActiva}</p>
        )}
        {p.estado === 'Activa' && (
          <p className="text-xs text-ink-muted" data-print="hidden">
            Una plantilla activa no se edita: para cambiarla, suspéndela y prepara otra.
          </p>
        )}
        {aviso && <p role="status" className="rounded-md bg-success-bg px-3 py-2.5 text-sm text-success-fg">{aviso}</p>}
        {mensaje && <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">{mensaje}</p>}
        {resultado && <ResultadoGeneracion resultado={resultado} onCerrar={() => setResultado(null)} mostrarPlantilla={false} />}

        <dl className="grid grid-cols-2 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat md:grid-cols-4">
          <Dato k="Tipo de operación"><span className="font-mono text-xs">{p.tipoClave}</span></Dato>
          <Dato k="Inicio">{etiquetaMes(p.anioInicio, p.mesInicio)}</Dato>
          <Dato k="Periodos">{p.numeroPeriodos}</Dato>
          <Dato k="Día de generación">{etiquetaDia(p.diaDelMes)}</Dato>
          <Dato k="Reversa automática">{p.reversaAutomatica ? 'Sí, el día 1 del mes siguiente' : 'No'}</Dato>
          <Dato k="Las pólizas nacen">{p.naceAutorizada ? 'Autorizadas' : 'En borrador (por autorizar)'}</Dato>
          <Dato k="Importe por póliza"><span className="tabular-nums">{monto(p.importe)} MXN</span></Dato>
          <Dato k="Generadas">{p.generadas} de {p.numeroPeriodos}</Dato>
          <Dato k="Preparó">{p.creadaPor}</Dato>
          <Dato k="Activó">{p.activadaPor ?? '—'}</Dato>
          <div className="col-span-2 md:col-span-2">
            <dt className="text-xs text-ink-muted">Concepto</dt>
            <dd className="whitespace-pre-line text-sm font-medium">{p.concepto}</dd>
          </div>
        </dl>

        <TablaPartidas partidas={p.partidas} />
        <Calendario aplicaciones={p.aplicaciones} reversaAutomatica={p.reversaAutomatica} />

        <section aria-label="Actividad" className="rounded-lg bg-surface-card p-4 shadow-card-flat">
          <h2 className="mb-2 text-md font-semibold">Actividad</h2>
          <ul className="flex flex-col gap-2 text-sm">
            {actividad(p).map((a) => (
              <li key={a.texto} className="flex items-center gap-2">
                <span className="size-2 shrink-0 rounded-full bg-line-control" aria-hidden="true" />
                <span className="text-ink-strong">{a.texto}</span>
                <span className="ml-auto text-xs text-ink-muted">{formatDateTime(a.en)}</span>
              </li>
            ))}
          </ul>
        </section>
      </div>

      {p.estado === 'Borrador' && (
        <PlantillaSheet open={editando} onOpenChange={setEditando} plantilla={p} onGuardada={() => setAviso('Plantilla guardada.')} />
      )}
    </div>
  );
}

function Dato({ k, children }: { k: string; children: React.ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs text-ink-muted">{k}</dt>
      <dd className="text-sm font-medium">{children}</dd>
    </div>
  );
}

function actividad(p: PlantillaRecurrente): { texto: string; en: string }[] {
  const filas = [{ texto: `Preparó ${p.creadaPor}`, en: p.creadaEn }];
  if (p.activadaEn) filas.push({ texto: `Activó ${p.activadaPor ?? '[USUARIO]'}`, en: p.activadaEn });
  return filas.sort((a, b) => b.en.localeCompare(a.en));
}

function centros(x: PartidaPlantilla): string {
  return [
    x.dim1?.clave ?? (x.dim1Id ? '[CENTRO]' : null),
    x.dim2?.clave ?? (x.dim2Id ? '[CENTRO]' : null),
    x.dim3?.clave ?? (x.dim3Id ? '[CENTRO]' : null),
  ].filter(Boolean).join(' · ') || '—';
}

function TablaPartidas({ partidas }: { partidas: PartidaPlantilla[] }) {
  const etiquetas = useEtiquetasCuentas(partidas.map((x) => x.cuentaId));
  const sucursales = useSucursalesMovimiento();
  const nombreSucursal = (x: PartidaPlantilla) =>
    x.sucursalNombre ?? sucursales.data?.find((s) => s.id === x.sucursalId)?.nombre ?? '[SUCURSAL]';
  const totalCargos = partidas.reduce((s, x) => s + x.cargo, 0);
  const totalAbonos = partidas.reduce((s, x) => s + x.abono, 0);
  return (
    <section aria-label="Partidas" className="overflow-hidden rounded-lg bg-surface-card shadow-card-flat">
      <h2 className="border-b border-line-divider px-4 py-3 text-md font-semibold">Partidas fijas</h2>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[760px] text-sm">
          <thead className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
            <tr>
              <th className="h-9 px-3">#</th><th className="px-3">Cuenta</th><th className="px-3">Concepto</th><th className="px-3">Sucursal</th>
              <th className="px-3">Centros</th><th className="px-3 text-right">Cargo</th><th className="px-3 text-right">Abono</th>
            </tr>
          </thead>
          <tbody>
            {partidas.map((x, i) => {
              const cuenta = x.cuentaCodigo ? { codigo: x.cuentaCodigo, nombre: x.cuentaNombre ?? '' } : etiquetas.get(x.cuentaId);
              return (
                <tr key={`${i}-${x.cuentaId}`} className="border-b border-line-row align-top">
                  <td className="px-3 py-2 text-xs text-ink-muted">{i + 1}</td>
                  <td className="px-3 py-2">
                    <span className="font-mono text-xs">{cuenta?.codigo ?? '[CUENTA]'}</span>
                    {cuenta?.nombre && <span className="block text-xs text-ink-muted">{cuenta.nombre}</span>}
                  </td>
                  <td className="px-3 py-2">{x.concepto ?? '—'}</td>
                  <td className="px-3 py-2">{nombreSucursal(x)}</td>
                  <td className="px-3 py-2 font-mono text-xs">
                    {centros(x)}
                    {x.proyecto && <span className="block font-sans text-ink-muted">Proyecto {x.proyecto}</span>}
                  </td>
                  <td className="whitespace-nowrap px-3 py-2 text-right font-medium tabular-nums">{importeLado(x.cargo)}</td>
                  <td className="whitespace-nowrap px-3 py-2 text-right font-medium tabular-nums">{importeLado(x.abono)}</td>
                </tr>
              );
            })}
          </tbody>
          <tfoot>
            <tr className="bg-surface-subtle font-semibold">
              <td className="px-3 py-2" colSpan={5}>Totales</td>
              <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums">{monto(totalCargos)}</td>
              <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums">{monto(totalAbonos)}</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </section>
  );
}

function Calendario({ aplicaciones, reversaAutomatica }: { aplicaciones: AplicacionRecurrente[]; reversaAutomatica: boolean }) {
  return (
    <section aria-label="Calendario de aplicaciones" className="overflow-hidden rounded-lg bg-surface-card shadow-card-flat">
      <h2 className="border-b border-line-divider px-4 py-3 text-md font-semibold">Calendario de aplicaciones</h2>
      {aplicaciones.length === 0 ? (
        <p className="p-4 text-sm text-ink-muted">Sin aplicaciones programadas.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[720px] text-sm">
            <thead className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
              <tr>
                <th className="h-9 px-3">Periodo</th><th className="px-3">Fecha</th><th className="px-3">Póliza</th><th className="px-3">Estado</th>
                {reversaAutomatica && <><th className="px-3">Fecha de reversa</th><th className="px-3">Reversa</th></>}
              </tr>
            </thead>
            <tbody>
              {aplicaciones.map((a) => (
                <tr key={`${a.anio}-${a.mes}`} className="h-12 border-b border-line-row">
                  <td className="whitespace-nowrap px-3">{etiquetaMes(a.anio, a.mes)}</td>
                  <td className="whitespace-nowrap px-3 text-ink-secondary">{fechaCorta(a.fecha)}</td>
                  <td className="px-3">
                    {a.polizaId ? (
                      <Link
                        to="/contabilidad/polizas/$id" params={{ id: a.polizaId }}
                        className={cn('text-xs font-medium text-brand hover:underline', a.folio && 'font-mono')}
                        aria-label={`Ver póliza ${folioOBorrador(a.folio)} de ${etiquetaMes(a.anio, a.mes)}`}
                      >
                        {folioOBorrador(a.folio)}
                      </Link>
                    ) : (
                      <span className="text-xs text-ink-muted">Por generar</span>
                    )}
                  </td>
                  <td className="px-3">{a.estado ? <EstadoPolizaBadge estado={a.estado} /> : '—'}</td>
                  {reversaAutomatica && (
                    <>
                      <td className="whitespace-nowrap px-3 text-ink-secondary">{a.fechaReversa ? fechaCorta(a.fechaReversa) : '—'}</td>
                      <td className="px-3">
                        {a.reversaId ? (
                          <Link
                            to="/contabilidad/polizas/$id" params={{ id: a.reversaId }}
                            className={cn('text-xs font-medium text-brand hover:underline', a.reversaFolio && 'font-mono')}
                            aria-label={`Ver reversa ${folioOBorrador(a.reversaFolio)} de ${etiquetaMes(a.anio, a.mes)}`}
                          >
                            {folioOBorrador(a.reversaFolio)}
                          </Link>
                        ) : (
                          <span className="text-xs text-ink-muted">{a.fechaReversa ? 'Por generar' : '—'}</span>
                        )}
                      </td>
                    </>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
