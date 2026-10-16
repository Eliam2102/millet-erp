import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Ban, CheckCheck, Pencil, Printer, Trash2, X } from 'lucide-react';
import { DocumentoAdjuntosSection } from '@/components/erp/adjuntos/DocumentoAdjuntosSection';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Skeleton } from '@/components/ui/skeleton';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { formatDateTime } from '@/lib/datetime';
import { cn } from '@/lib/utils';
import { useAutorizarPoliza, useDescartarPoliza, usePoliza, usePolizas } from '../api/polizas';
import type { Partida, Poliza } from '../api/polizas-types';
import { AnularPolizaDialog } from '../components/polizas/AnularPolizaDialog';
import { CuadrePoliza } from '../components/polizas/CuadrePoliza';
import { EstadoPolizaBadge } from '../components/polizas/EstadoPolizaBadge';
import { PolizaSheet } from '../components/polizas/PolizaSheet';
import { fechaCorta } from '../lib/dimensiones';
import { etiquetaPeriodo, folioOBorrador, importeLado, mensajePoliza, monto, type PolizasSearch } from '../lib/polizas';

const PERMISOS_ADJUNTOS = {
  ver: PermisosCanonicos.ContabilidadPolizasLeer,
  subir: PermisosCanonicos.ContabilidadPolizasCapturar,
  baja: PermisosCanonicos.ContabilidadPolizasCapturar,
};

interface Props {
  id: string;
  search: PolizasSearch;
  /** Navega a otra póliza (anulación recién creada) o, con null, de vuelta a la bandeja (borrador descartado). */
  onNavegar: (id: string | null) => void;
}

/** Detalle de póliza (P3 master-detail, F1-CON-04): lista compacta a la izquierda y el detalle con sus acciones. */
export function PolizaDetallePage({ id, search, onNavegar }: Props) {
  return (
    <div className="flex min-h-0 flex-1">
      <ListaCompacta idActivo={id} search={search} />
      <div className="min-w-0 flex-1">
        <DetallePoliza id={id} search={search} onNavegar={onNavegar} />
      </div>
    </div>
  );
}

function ListaCompacta({ idActivo, search }: { idActivo: string; search: PolizasSearch }) {
  const lista = usePolizas({
    anio: search.anio, numero: search.anio ? search.numero : undefined, tipoOperacionId: search.tipo, estado: search.estado,
    q: search.q || undefined, offset: 0, limit: 50,
  });
  return (
    <aside
      aria-label="Pólizas"
      className="sticky top-0 hidden max-h-[calc(100vh-56px)] w-80 shrink-0 overflow-y-auto border-r border-line bg-surface-card lg:block"
      data-print="hidden"
    >
      <p className="border-b border-line-divider px-3.5 py-3 text-xs text-ink-muted">
        {lista.data ? `${lista.data.total} ${lista.data.total === 1 ? 'póliza' : 'pólizas'}` : 'Cargando…'}
      </p>
      <ul>
        {(lista.data?.items ?? []).map((p) => (
          <li key={p.id}>
            <Link
              to="/contabilidad/polizas/$id" params={{ id: p.id }} search={search}
              aria-current={p.id === idActivo ? 'page' : undefined}
              className={cn('block border-b border-line-row px-3.5 py-[11px] text-sm', p.id === idActivo && 'bg-surface-selected shadow-list-selected')}
            >
              <span className="flex items-center justify-between gap-2">
                <span className={cn('text-xs font-medium', p.folio ? 'font-mono text-brand' : 'text-ink-secondary')}>{folioOBorrador(p.folio)}</span>
                <span className="text-xs font-medium tabular-nums">{monto(p.totalCargos)}</span>
              </span>
              <span className="mt-0.5 flex items-center justify-between gap-2 text-xs text-ink-muted">
                <span className="truncate">{p.concepto}</span>
                <span className="shrink-0">{fechaCorta(p.fecha)}</span>
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </aside>
  );
}

function DetallePoliza({ id, search, onNavegar }: Props) {
  const consulta = usePoliza(id);
  const p = consulta.data;
  const usuarioId = useAuthStore((s) => s.user?.id ?? null);
  const puedeCapturar = useHasPermission(PermisosCanonicos.ContabilidadPolizasCapturar);
  const puedeAutorizar = useHasPermission(PermisosCanonicos.ContabilidadPolizasAutorizar);
  const puedeAnular = useHasPermission(PermisosCanonicos.ContabilidadPolizasAnular);
  const autorizar = useAutorizarPoliza();
  const descartar = useDescartarPoliza();
  const keyAutorizar = useFormIdempotencyKey();
  const keyDescartar = useFormIdempotencyKey();
  const [editando, setEditando] = useState(false);
  const [confirmarDescarte, setConfirmarDescarte] = useState(false);
  const [anulando, setAnulando] = useState(false);
  const [mensaje, setMensaje] = useState('');
  const [aviso, setAviso] = useState('');

  const cerrar = (
    <Button asChild variant="ghost" size="icon" aria-label="Cerrar y volver a la bandeja">
      <Link to="/contabilidad/polizas" search={search}><X className="size-4" aria-hidden="true" /></Link>
    </Button>
  );

  if (consulta.isLoading) {
    return (
      <div className="space-y-3 px-4 py-5 sm:px-7" aria-label="Cargando póliza">
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
          {noExiste ? 'La póliza no existe o fue descartada.' : `No se pudo cargar la póliza. ${mensajePoliza(consulta.error)}`}
          {!noExiste && <Button variant="ghost" size="sm" onClick={() => void consulta.refetch()}>Reintentar</Button>}
        </div>
      </div>
    );
  }

  const esBorrador = p.estado === 'Borrador';
  const esAnulacion = !!p.polizaAnuladaId;
  const capturoElUsuario = !!usuarioId && !!p.capturadaPorId && usuarioId === p.capturadaPorId;
  const motivoNoAutoriza = capturoElUsuario
    ? 'No puedes autorizar una póliza que tú capturaste: la autoriza otro usuario.'
    : !p.cuadra ? 'La póliza no cuadra: cargos y abonos deben ser iguales y distintos de cero.' : undefined;

  function onAutorizar(poliza: Poliza) {
    setMensaje(''); setAviso('');
    autorizar.mutate(
      { poliza, idempotencyKey: keyAutorizar },
      { onSuccess: (r) => setAviso(`Póliza autorizada con folio ${r.folio ?? ''}.`), onError: (e) => setMensaje(mensajePoliza(e)) },
    );
  }

  function onDescartar(poliza: Poliza) {
    setMensaje('');
    descartar.mutate(
      { poliza, idempotencyKey: keyDescartar },
      {
        onSuccess: () => { setConfirmarDescarte(false); onNavegar(null); },
        onError: (e) => { setConfirmarDescarte(false); setMensaje(mensajePoliza(e)); },
      },
    );
  }

  return (
    <div className="flex flex-col">
      <div
        className="sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b border-line bg-surface-card px-4 py-2 sm:px-7"
        data-print="hidden"
      >
        <span className={cn('text-sm font-medium', p.folio && 'font-mono')}>{folioOBorrador(p.folio)}</span>
        <EstadoPolizaBadge estado={p.estado} esAnulacion={esAnulacion} />
        <span className="ml-auto flex flex-wrap items-center gap-2">
          {esBorrador && puedeCapturar && (
            <>
              <Button variant="secondary" onClick={() => setEditando(true)}><Pencil className="mr-1 size-3.5" aria-hidden="true" />Editar</Button>
              <Button variant="secondary-danger" onClick={() => setConfirmarDescarte(true)}><Trash2 className="mr-1 size-3.5" aria-hidden="true" />Descartar</Button>
            </>
          )}
          {esBorrador && puedeAutorizar && (
            <Button disabled={!!motivoNoAutoriza || autorizar.isPending} title={motivoNoAutoriza} onClick={() => onAutorizar(p)}>
              <CheckCheck className="mr-1 size-3.5" aria-hidden="true" />{autorizar.isPending ? 'Autorizando…' : 'Autorizar'}
            </Button>
          )}
          {p.estado === 'Autorizada' && !esAnulacion && puedeAnular && (
            <Button variant="secondary-danger" onClick={() => { setMensaje(''); setAviso(''); setAnulando(true); }}>
              <Ban className="mr-1 size-3.5" aria-hidden="true" />Anular
            </Button>
          )}
          <Button variant="ghost" size="icon" aria-label="Imprimir póliza" onClick={() => window.print()}>
            <Printer className="size-4" strokeWidth={1.6} aria-hidden="true" />
          </Button>
          {cerrar}
        </span>
      </div>

      <div className="flex flex-col gap-5 px-4 py-6 sm:px-7">
        <header className="flex flex-col gap-1">
          <h1 className="flex flex-wrap items-center gap-2 text-3xl font-semibold">
            <span className={p.folio ? 'font-mono' : undefined}>{p.folio ?? 'Borrador'}</span>
            <EstadoPolizaBadge estado={p.estado} esAnulacion={esAnulacion} />
          </h1>
          <p className="text-sm text-ink-secondary">
            <span className="font-mono text-xs">{p.tipoClave}</span> {p.tipoNombre ?? ''} · Tipo SAT {p.tipoSat}
            {esBorrador && <span className="text-ink-muted"> · El folio se asigna al autorizar.</span>}
          </p>
        </header>

        {aviso && <p role="status" className="rounded-md bg-success-bg px-3 py-2.5 text-sm text-success-fg">{aviso}</p>}
        {mensaje && <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">{mensaje}</p>}

        {p.anuladaPorPolizaId && (
          <section aria-label="Anulación" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">
            <p className="font-medium">
              Anulada por{' '}
              <Link to="/contabilidad/polizas/$id" params={{ id: p.anuladaPorPolizaId }} search={search} className="font-mono underline">
                {p.anuladaPorFolio ?? 'póliza de anulación'}
              </Link>
            </p>
            {p.motivoAnulacion && <p className="mt-1">Motivo: {p.motivoAnulacion}</p>}
            {(p.anuladaPor || p.anuladaEn) && (
              <p className="mt-1 text-xs">{p.anuladaPor ?? '[USUARIO]'}{p.anuladaEn ? ` · ${formatDateTime(p.anuladaEn)}` : ''}</p>
            )}
          </section>
        )}
        {p.polizaAnuladaId && (
          <section aria-label="Póliza anulada" className="rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg">
            <p className="font-medium">
              Anula a{' '}
              <Link to="/contabilidad/polizas/$id" params={{ id: p.polizaAnuladaId }} search={search} className="font-mono underline">
                {p.polizaAnuladaFolio ?? 'póliza original'}
              </Link>
              : mismas partidas con importes en negativo.
            </p>
            {p.motivoAnulacion && <p className="mt-1">Motivo: {p.motivoAnulacion}</p>}
          </section>
        )}

        <dl className="grid grid-cols-2 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat md:grid-cols-4">
          <Dato k="Tipo de operación"><span className="font-mono text-xs">{p.tipoClave}</span> {p.tipoNombre ?? ''}</Dato>
          <Dato k="Tipo SAT">{p.tipoSat}</Dato>
          <Dato k="Fecha">{fechaCorta(p.fecha)}</Dato>
          <Dato k="Periodo">{etiquetaPeriodo(p.periodoAnio, p.periodoNumero)}</Dato>
          <Dato k="Origen">{p.origen === 'Automatica' ? 'Automática' : 'Manual'}</Dato>
          <Dato k="Documento origen">
            {p.documentoOrigen
              ? <>{p.documentoOrigen.modulo} · {p.documentoOrigen.tipo} {p.documentoOrigen.folio && <span className="font-mono text-xs">{p.documentoOrigen.folio}</span>}</>
              : '—'}
          </Dato>
          <Dato k="Capturó">{p.capturadaPor}</Dato>
          <Dato k="Autorizó">{p.autorizadaPor ?? '—'}</Dato>
          <div className="col-span-2 md:col-span-4">
            <dt className="text-xs text-ink-muted">Concepto</dt>
            <dd className="whitespace-pre-line text-sm font-medium">{p.concepto}</dd>
          </div>
        </dl>

        <TablaPartidas poliza={p} />
        <CuadrePoliza totalCargos={p.totalCargos} totalAbonos={p.totalAbonos} />

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

        <DocumentoAdjuntosSection base={`/api/v1/contabilidad/polizas/${p.id}`} tipo="poliza" permisos={PERMISOS_ADJUNTOS} soloLectura={!esBorrador} />
      </div>

      {esBorrador && (
        <PolizaSheet open={editando} onOpenChange={setEditando} poliza={p} onGuardada={() => setAviso('Borrador guardado.')} />
      )}

      <Dialog open={confirmarDescarte} onOpenChange={(o) => { if (!o && !descartar.isPending) setConfirmarDescarte(false); }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Descartar borrador</DialogTitle>
            <DialogDescription>
              El borrador se elimina con sus partidas y adjuntos. Como aún no tiene folio, no deja hueco en el consecutivo.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setConfirmarDescarte(false)} disabled={descartar.isPending}>Cancelar</Button>
            <Button variant="secondary-danger" onClick={() => onDescartar(p)} disabled={descartar.isPending}>
              {descartar.isPending ? 'Descartando…' : 'Descartar borrador'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <AnularPolizaDialog
        poliza={anulando ? p : null}
        onClose={() => setAnulando(false)}
        onAnulada={(nueva) => { setAnulando(false); onNavegar(nueva.id); }}
      />
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

function actividad(p: Poliza): { texto: string; en: string }[] {
  const filas = [{ texto: `Capturó ${p.capturadaPor}`, en: p.capturadaEn }];
  if (p.autorizadaEn) filas.push({ texto: `Autorizó ${p.autorizadaPor ?? '[USUARIO]'}`, en: p.autorizadaEn });
  if (p.anuladaEn) filas.push({ texto: `Anuló ${p.anuladaPor ?? '[USUARIO]'}`, en: p.anuladaEn });
  return filas.sort((a, b) => b.en.localeCompare(a.en));
}

function centros(x: Partida): string {
  return [x.dim1, x.dim2, x.dim3].filter(Boolean).map((c) => c!.clave).join(' · ') || '—';
}

function otras(x: Partida): string {
  return [
    x.proyecto && `Proyecto ${x.proyecto}`,
    x.cliente && `Cliente ${x.cliente.clave}`,
    x.proveedor && `Proveedor ${x.proveedor.clave}`,
    x.cuentaBancaria && `Cuenta ${x.cuentaBancaria.clave}`,
  ].filter(Boolean).join(' · ');
}

function TablaPartidas({ poliza }: { poliza: Poliza }) {
  const conMoneda = poliza.partidas.some((x) => x.moneda && x.moneda !== 'MXN');
  return (
    <section aria-label="Partidas" className="overflow-hidden rounded-lg bg-surface-card shadow-card-flat">
      <h2 className="border-b border-line-divider px-4 py-3 text-md font-semibold">Partidas</h2>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[880px] text-sm">
          <thead className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
            <tr>
              <th className="h-9 px-3">#</th><th className="px-3">Cuenta</th><th className="px-3">Concepto</th><th className="px-3">Sucursal</th>
              <th className="px-3">Centros</th>
              {conMoneda && <th className="px-3">Moneda / TC</th>}
              <th className="px-3 text-right">Cargo</th><th className="px-3 text-right">Abono</th>
            </tr>
          </thead>
          <tbody>
            {poliza.partidas.map((x) => (
              <tr key={x.id} className="border-b border-line-row align-top">
                <td className="px-3 py-2 text-xs text-ink-muted">{x.linea}</td>
                <td className="px-3 py-2">
                  <span className="font-mono text-xs">{x.cuentaCodigo}</span>
                  <span className="block text-xs text-ink-muted">{x.cuentaNombre ?? ''}</span>
                </td>
                <td className="px-3 py-2">{x.concepto ?? '—'}</td>
                <td className="px-3 py-2">{x.sucursalNombre ?? '[SUCURSAL]'}</td>
                <td className="px-3 py-2 font-mono text-xs">
                  {centros(x)}
                  {otras(x) && <span className="block font-sans text-ink-muted">{otras(x)}</span>}
                </td>
                {conMoneda && (
                  <td className="px-3 py-2 text-xs tabular-nums">
                    {x.moneda !== 'MXN' ? `${x.moneda} · TC ${x.tipoCambio}${x.importeOriginal != null ? ` · ${monto(x.importeOriginal)}` : ''}` : 'MXN'}
                  </td>
                )}
                <td className={cn('whitespace-nowrap px-3 py-2 text-right font-medium tabular-nums', x.cargo < 0 && 'text-danger-fg')}>{importeLado(x.cargo)}</td>
                <td className={cn('whitespace-nowrap px-3 py-2 text-right font-medium tabular-nums', x.abono < 0 && 'text-danger-fg')}>{importeLado(x.abono)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="bg-surface-subtle font-semibold">
              <td className="px-3 py-2" colSpan={conMoneda ? 6 : 5}>Totales</td>
              <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums">{monto(poliza.totalCargos)}</td>
              <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums">{monto(poliza.totalAbonos)}</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </section>
  );
}
