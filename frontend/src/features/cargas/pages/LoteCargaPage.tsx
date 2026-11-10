import { useState } from 'react';
import { toast } from 'sonner';
import { Upload } from 'lucide-react';
import { PageHeader } from '@/components/erp/PageHeader';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { mensajeErrorAccion } from '@/lib/api/error';
import { SELECT_CLASS } from '@/features/contabilidad/lib/estilos';
import {
  useAccionLote,
  useEncabezadosArchivo,
  useLoteCarga,
  useLotesCarga,
  useSubirLote,
  useTiposCarga,
  type EncabezadosArchivo,
  type EstadoLoteCarga,
  type LoteCargaDetalle,
  type TipoCarga,
} from '../api/lotes';

const ESTADO: Record<EstadoLoteCarga, { texto: string; variant: 'info' | 'success' | 'danger' | 'neutral' }> = {
  Subido: { texto: 'Por autorizar', variant: 'info' },
  Autorizado: { texto: 'Autorizado, sin aplicar', variant: 'neutral' },
  Rechazado: { texto: 'Rechazado', variant: 'danger' },
  Aplicado: { texto: 'Aplicado', variant: 'success' },
};

function fechaHora(iso: string | null) {
  return iso ? new Date(iso).toLocaleString('es-MX', { dateStyle: 'short', timeStyle: 'short' }) : '—';
}

/**
 * M-167 (86e3jvvmf): pantalla del lote común de cargas (M-047) para un tipo. Paso 1: subir el archivo y la fecha
 * de corte; paso 2: elegir qué columna del archivo corresponde a cada dato; paso 3: vista previa por renglón con
 * los errores del módulo dueño. Otra persona autoriza (y se aplica) o rechaza con motivo.
 */
export function LoteCargaPage({ tipo, titulo, descripcion }: { tipo: string; titulo: string; descripcion: string }) {
  const tipos = useTiposCarga();
  const definicion = tipos.data?.find((t) => t.tipo === tipo);
  const lotes = useLotesCarga(tipo, Boolean(definicion && (definicion.puedeSubir || definicion.puedeAutorizar)));
  const [seleccionado, setSeleccionado] = useState<string | null>(null);
  const [nueva, setNueva] = useState(false);

  return (
    <div className="space-y-5">
      <PageHeader
        titulo={titulo}
        descripcion={descripcion}
        acciones={
          definicion?.puedeSubir ? (
            <Button size="sm" onClick={() => { setNueva(true); setSeleccionado(null); }}>
              <Upload className="mr-1.5 h-4 w-4" aria-hidden="true" /> Nueva carga
            </Button>
          ) : null
        }
      />
      {/* QA O1: el motivo correcto, sin el error técnico del servidor. */}
      {tipos.isSuccess && !definicion && (
        <Alert><AlertDescription>Este tipo de carga aún no está habilitado. Cuando el área responsable lo active, aquí podrá subir sus archivos.</AlertDescription></Alert>
      )}
      {definicion && !definicion.puedeSubir && !definicion.puedeAutorizar && (
        <Alert><AlertDescription>Su usuario no tiene permiso para esta carga. Pida el acceso al administrador.</AlertDescription></Alert>
      )}
      {nueva && definicion && (
        <NuevaCarga definicion={definicion} onCancelar={() => setNueva(false)}
          onSubido={(d) => { setNueva(false); setSeleccionado(d.lote.id); }} />
      )}
      {seleccionado && <DetalleLote id={seleccionado} tipo={tipo} definicion={definicion} />}
      {definicion && (definicion.puedeSubir || definicion.puedeAutorizar) && (
      <section className="space-y-2">
        <h2 className="text-sm font-medium">Lotes ({lotes.data?.length ?? 0})</h2>
        {lotes.isError ? (
          <Alert variant="destructive"><AlertDescription>{mensajeErrorAccion(lotes.error)}</AlertDescription></Alert>
        ) : (lotes.data ?? []).length === 0 ? (
          <p className="rounded-md border border-dashed border-line px-3 py-4 text-center text-xs text-ink-muted">
            Todavía no hay cargas de este tipo.
          </p>
        ) : (
          <div className="overflow-x-auto rounded-md border border-line">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50">
                <TableRow>
                  <TableHead>Archivo</TableHead><TableHead>Corte</TableHead><TableHead className="text-right">Renglones</TableHead>
                  <TableHead className="text-right">Errores</TableHead><TableHead>Estado</TableHead><TableHead>Subió</TableHead>
                  <TableHead>Resultado</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {lotes.data!.map((l) => (
                  <TableRow key={l.id} className="cursor-pointer" onClick={() => { setSeleccionado(l.id); setNueva(false); }}>
                    <TableCell><button type="button" className="text-left text-brand hover:underline">{l.nombreArchivo}</button></TableCell>
                    <TableCell>{l.fechaCorte ?? '—'}</TableCell>
                    <TableCell className="text-right tabular-nums">{l.totalFilas}</TableCell>
                    <TableCell className="text-right tabular-nums">{l.totalErrores}</TableCell>
                    <TableCell><Badge variant={ESTADO[l.estado].variant}>{ESTADO[l.estado].texto}</Badge></TableCell>
                    <TableCell>{l.subidoPor}<p className="text-xs text-ink-muted">{fechaHora(l.subidoEn)}</p></TableCell>
                    <TableCell className="font-mono text-xs">{l.folioResultado ?? (l.ultimoError ? 'Detenido' : '—')}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </section>
      )}
    </div>
  );
}

function NuevaCarga({ definicion, onCancelar, onSubido }: {
  definicion: TipoCarga; onCancelar: () => void; onSubido: (d: LoteCargaDetalle) => void;
}) {
  const [archivo, setArchivo] = useState<File | null>(null);
  const [fechaCorte, setFechaCorte] = useState('');
  const [encabezados, setEncabezados] = useState<EncabezadosArchivo | null>(null);
  // Columna del tipo → encabezado del archivo (lo que elige el usuario).
  const [eleccion, setEleccion] = useState<Record<string, string>>({});
  // Parámetros del tipo (p. ej. sucursal por omisión), con su valor por omisión.
  const [parametros, setParametros] = useState<Record<string, string>>(() =>
    Object.fromEntries((definicion.parametros ?? []).map((p) => [p.nombre, p.porOmision ?? ''])));
  const leer = useEncabezadosArchivo(definicion.tipo);
  const subir = useSubirLote(definicion.tipo);

  async function leerEncabezados(f: File) {
    setArchivo(f); setEncabezados(null);
    try {
      const r = await leer.mutateAsync(f);
      setEncabezados(r);
      setEleccion(Object.fromEntries(Object.entries(r.mapeoSugerido).map(([enc, col]) => [col, enc])));
    } catch (e) { toast.error(mensajeErrorAccion(e)); }
  }

  const faltan = definicion.columnas.filter((c) => c.obligatoria && !eleccion[c.nombre]).map((c) => c.nombre);
  const sinFecha = definicion.requiereFechaCorte && fechaCorte === '';
  const parametrosFaltantes = (definicion.parametros ?? []).filter((p) => p.obligatorio && !parametros[p.nombre]);

  async function validar() {
    if (!archivo) return;
    const mapeo = Object.fromEntries(Object.entries(eleccion).filter(([, enc]) => enc).map(([col, enc]) => [enc, col]));
    const capturados = Object.fromEntries(Object.entries(parametros).filter(([, v]) => v !== ''));
    try { onSubido(await subir.mutateAsync({ archivo, fechaCorte: fechaCorte || null, mapeo, parametros: capturados })); }
    catch (e) { toast.error(mensajeErrorAccion(e)); }
  }

  return (
    <section className="space-y-4 rounded-md border border-dashed border-brand/50 p-4" aria-label="Nueva carga">
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="space-y-1">
          <Label htmlFor="archivo-carga">1. Archivo (.xlsx o .csv)</Label>
          <Input id="archivo-carga" type="file" accept=".xlsx,.csv"
            onChange={(e) => { const f = e.target.files?.[0]; if (f) void leerEncabezados(f); }} />
        </div>
        {definicion.requiereFechaCorte && (
          <div className="space-y-1">
            <Label htmlFor="fecha-corte">Fecha de corte</Label>
            <Input id="fecha-corte" type="date" value={fechaCorte} onChange={(e) => setFechaCorte(e.target.value)} />
          </div>
        )}
        {(definicion.parametros ?? []).map((p) => (
          <div key={p.nombre} className="space-y-1">
            <Label htmlFor={`param-${p.nombre}`}>{p.etiqueta}{p.obligatorio ? ' *' : ''}</Label>
            {p.opciones && p.opciones.length > 0 ? (
              <select id={`param-${p.nombre}`} className={SELECT_CLASS + ' w-full'} value={parametros[p.nombre] ?? ''}
                onChange={(e) => setParametros((x) => ({ ...x, [p.nombre]: e.target.value }))}>
                {!p.obligatorio && <option value="">— Sin valor —</option>}
                {p.opciones.map((o) => <option key={o.valor} value={o.valor}>{o.etiqueta}</option>)}
              </select>
            ) : (
              <Input id={`param-${p.nombre}`} value={parametros[p.nombre] ?? ''}
                onChange={(e) => setParametros((x) => ({ ...x, [p.nombre]: e.target.value }))} />
            )}
            <p className="text-xs text-ink-muted">{p.ayuda}</p>
          </div>
        ))}
      </div>
      {encabezados && (
        <div className="space-y-2">
          <p className="text-sm font-medium">2. ¿Qué columna de su archivo corresponde a cada dato?</p>
          <div className="grid gap-3 sm:grid-cols-3">
            {definicion.columnas.map((c) => (
              <div key={c.nombre} className="space-y-1">
                <Label htmlFor={`col-${c.nombre}`}>{c.nombre}{c.obligatoria ? ' *' : ''}</Label>
                <select id={`col-${c.nombre}`} className={SELECT_CLASS + ' w-full'} value={eleccion[c.nombre] ?? ''}
                  onChange={(e) => setEleccion((x) => ({ ...x, [c.nombre]: e.target.value }))}>
                  <option value="">— Sin columna —</option>
                  {encabezados.encabezados.filter((h) => h).map((h) => <option key={h} value={h}>{h}</option>)}
                </select>
                <p className="text-xs text-ink-muted">{c.ayuda}</p>
              </div>
            ))}
          </div>
        </div>
      )}
      {encabezados && faltan.length > 0 && (
        <p className="text-xs text-danger-fg">Elija la columna para: {faltan.join(', ')}.</p>
      )}
      <div className="flex gap-2">
        <Button size="sm" disabled={!encabezados || faltan.length > 0 || sinFecha || parametrosFaltantes.length > 0 || subir.isPending} onClick={() => void validar()}>
          3. Validar y ver vista previa
        </Button>
        <Button size="sm" variant="outline" onClick={onCancelar}>Cancelar</Button>
      </div>
    </section>
  );
}

function DetalleLote({ id, tipo, definicion }: { id: string; tipo: string; definicion?: TipoCarga }) {
  const detalle = useLoteCarga(id);
  const accion = useAccionLote(tipo);
  const [rechazando, setRechazando] = useState(false);
  const [motivo, setMotivo] = useState('');
  if (detalle.isLoading) return <div className="h-24 animate-pulse rounded bg-surface-muted" />;
  if (detalle.isError || !detalle.data) {
    return <Alert variant="destructive"><AlertDescription>{mensajeErrorAccion(detalle.error)}</AlertDescription></Alert>;
  }
  const d = detalle.data;
  const columnas = definicion?.columnas.map((c) => c.nombre) ?? Object.keys(d.filas[0]?.valores ?? {});

  async function ejecutar(a: 'autorizar' | 'rechazar' | 'reintentar') {
    try {
      const r = await accion.mutateAsync({ id, accion: a, motivo });
      if (a === 'rechazar') { setRechazando(false); setMotivo(''); toast.success('Lote rechazado.'); }
      else if (r.lote.estado === 'Aplicado') toast.success(`Carga aplicada${r.lote.folioResultado ? `: ${r.lote.folioResultado}` : ''}.`);
      else if (r.lote.ultimoError) toast.error(`Autorizado, pero no se aplicó: ${r.lote.ultimoError}`);
    } catch (e) { toast.error(mensajeErrorAccion(e)); }
  }

  return (
    <section className="space-y-3 rounded-md border border-line p-4" aria-label={`Lote ${d.lote.nombreArchivo}`}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h2 className="text-sm font-medium">{d.lote.nombreArchivo} <Badge variant={ESTADO[d.lote.estado].variant}>{ESTADO[d.lote.estado].texto}</Badge></h2>
          <p className="text-xs text-ink-muted">Subió {d.lote.subidoPor} · {fechaHora(d.lote.subidoEn)} · corte {d.lote.fechaCorte ?? '—'} · huella <span className="font-mono">{d.huella.slice(0, 12)}…</span></p>
        </div>
        <div className="flex gap-2">
          {d.puedeAutorizar && <Button size="sm" disabled={accion.isPending} onClick={() => void ejecutar('autorizar')}>Autorizar y aplicar</Button>}
          {d.puedeReintentar && <Button size="sm" variant="outline" disabled={accion.isPending} onClick={() => void ejecutar('reintentar')}>Reintentar aplicación</Button>}
          {d.puedeRechazar && <Button size="sm" variant="outline" onClick={() => setRechazando((x) => !x)}>Rechazar</Button>}
        </div>
      </div>
      {d.lote.estado === 'Subido' && !d.puedeAutorizar && d.lote.puedeAplicar && (
        <p className="text-xs text-ink-muted">Lo autoriza otra persona con permiso; quien subió el lote no puede autorizarlo.</p>
      )}
      {d.lote.ultimoError && <Alert variant="destructive"><AlertDescription>No se aplicó: {d.lote.ultimoError}</AlertDescription></Alert>}
      {d.motivoRechazo && <Alert><AlertDescription>Rechazado: {d.motivoRechazo}</AlertDescription></Alert>}
      {rechazando && (
        <div className="space-y-1">
          <Label htmlFor="motivo-rechazo">Motivo del rechazo</Label>
          <Textarea id="motivo-rechazo" value={motivo} onChange={(e) => setMotivo(e.target.value)} />
          <Button size="sm" variant="outline" disabled={motivo.trim().length < 5 || accion.isPending} onClick={() => void ejecutar('rechazar')}>Confirmar rechazo</Button>
        </div>
      )}
      {Object.keys(d.resumen).length > 0 && (
        <dl className="flex flex-wrap gap-4 text-sm">
          {Object.entries(d.resumen).map(([k, v]) => <div key={k}><dt className="text-xs text-ink-muted">{k}</dt><dd className="tabular-nums">{v}</dd></div>)}
        </dl>
      )}
      {d.erroresArchivo.length > 0 && (
        <Alert variant="destructive"><AlertDescription>
          <ul>{d.erroresArchivo.map((e, i) => <li key={i}>{e.mensaje}</li>)}</ul>
        </AlertDescription></Alert>
      )}
      <p className="text-xs text-ink-muted">
        {d.lote.totalFilas} renglones · {d.lote.totalErrores} errores
        {d.filasMostradas < d.lote.totalFilas ? ` · se muestran ${d.filasMostradas} (primero los que tienen error)` : ''}
      </p>
      <div className="max-h-96 overflow-auto rounded-md border border-line">
        <Table className="w-full text-sm">
          <TableHeader className="sticky top-0 bg-surface-muted">
            <TableRow>
              <TableHead className="text-right">Renglón</TableHead>
              {columnas.map((c) => <TableHead key={c}>{c}</TableHead>)}
              <TableHead>Errores</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {d.filas.map((f) => (
              <TableRow key={f.fila} className={f.errores.length > 0 ? 'bg-danger-bg/30' : undefined}>
                <TableCell className="text-right tabular-nums">{f.fila}</TableCell>
                {columnas.map((c) => <TableCell key={c} className="font-mono text-xs">{f.valores[c] || '—'}</TableCell>)}
                <TableCell className="text-xs text-danger-fg">{f.errores.map((e) => e.mensaje).join(' · ')}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      <details className="text-xs">
        <summary className="cursor-pointer text-ink-muted">Bitácora ({d.historial.length})</summary>
        <ul className="mt-1 space-y-0.5">
          {d.historial.map((h, i) => <li key={i}>{fechaHora(h.en)} · {h.accion} · {h.usuario}{h.detalle ? ` · ${h.detalle}` : ''}</li>)}
        </ul>
      </details>
    </section>
  );
}
