import { useState, type FormEvent } from 'react';
import { Camera } from 'lucide-react';
import { toast } from 'sonner';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { useSucursales } from '@/features/catalogos/api';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useAccionCaso, useEvidenciaCaso, type AccionCaso } from '../api/hooks';
import type { CasoDanoDto, DictamenCaso } from '../api/types';
import { mensajeApi } from '../lib/errores';
import { reducirFoto } from '../lib/imagen';
import { dictamenTexto, estadoCasoUi } from '../lib/casos-ui';
import { useNombreUsuario } from '../lib/usuarios';
import { etapaTexto } from '../lib/casos-ui';

const selectClass = 'h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3 text-sm';

const motivosSat = [
  { v: '01', t: '01 · Con errores, con relación (se sustituye)' },
  { v: '02', t: '02 · Con errores, sin relación' },
  { v: '03', t: '03 · No se llevó a cabo la operación' },
  { v: '04', t: '04 · Operación nominativa en factura global' },
] as const;
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Detalle de un caso de daño: evidencia, dictamen de Calidad, correctivo fiscal sobre la factura y cierre. */
export function CasoDetalle({ caso: c, operar }: { caso: CasoDanoDto; operar: boolean }) {
  const accion = useAccionCaso();
  const sucursales = useSucursales();
  const nombreUsuario = useNombreUsuario();
  const puedeDictaminar = useHasPermission(P.ProductoTerminadoCalidadDictaminar);
  const puedeNc = useHasPermission(P.FacturacionNotasCreditoBonificacion);
  const puedeCancelar = useHasPermission(P.FacturacionCancelacionesSolicitar);
  const evidencia = useEvidenciaCaso(c.id, c.tieneEvidencia);
  const [error, setError] = useState<string | null>(null);

  const ejecutar = (a: AccionCaso, ok: string) => {
    setError(null);
    return accion.mutateAsync(a).then(() => { toast.success(ok); }).catch((e) => { setError(mensajeApi(e)); throw e; });
  };
  const subirFoto = async (archivo: File | undefined) => {
    if (!archivo) return;
    let evid: string;
    try {
      evid = await reducirFoto(archivo);
    } catch {
      setError('No se pudo leer la foto. Elige otra imagen JPEG o PNG.');
      return;
    }
    await ejecutar({ tipo: 'evidencia', id: c.id, body: { evidencia: evid } }, 'Evidencia guardada').catch(() => undefined);
  };
  const sucursal = sucursales.data?.items.find((s) => s.id === c.sucursalId)?.nombre ?? 'Sin sucursal';
  const mostrarCorrectivo = c.estado === 'Dictaminado' && !!c.facturaFolio;

  return (
    <article aria-label={`Caso ${c.folio}`} className="flex flex-col gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <header className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h2 className="text-lg font-semibold"><span className="font-mono text-md">{c.folio}</span> · Pedido {c.numeroPedido}</h2>
          <p className="text-xs text-ink-muted">
            {etapaTexto[c.etapa]} · {sucursal} · registró {nombreUsuario(c.registradoPorId)} · <DateTimeDisplay value={c.registradoAt} variant="datetime" />
          </p>
        </div>
        <Badge variant={estadoCasoUi[c.estado].tono}>{estadoCasoUi[c.estado].texto}</Badge>
      </header>

      {error && <AvisoEnLinea tono="error" titulo="No se pudo completar la acción">{error}</AvisoEnLinea>}

      <section aria-label="Motivo y piezas" className="flex flex-col gap-2">
        <p>{c.motivo}</p>
        <ul className="flex flex-wrap gap-2">
          {c.piezas.map((p) => (
            <li key={p.etiqueta} className="rounded-md border border-line-divider px-2 py-1 text-xs">
              <span className="font-mono">{p.etiqueta}</span> · partida {p.posicion} · {p.estado}
            </li>
          ))}
        </ul>
        {c.facturaFolio && <p className="text-xs text-ink-muted">Factura relacionada: <span className="font-mono">{c.facturaFolio}</span></p>}
      </section>

      <section aria-label="Evidencia" className="flex flex-col gap-2">
        <h3 className="text-md font-semibold">Evidencia</h3>
        {c.tieneEvidencia ? (
          evidencia.isLoading ? <p className="text-ink-muted">Cargando evidencia…</p>
          : evidencia.isError ? <p className="text-danger-fg">No se pudo cargar la evidencia.</p>
          : evidencia.data?.foto ? <img src={evidencia.data.foto} alt={`Evidencia del caso ${c.folio}`} className="max-h-64 w-fit rounded-md border border-line object-contain" />
          : <p className="text-ink-muted">El caso tiene evidencia, pero no es una imagen.</p>
        ) : (
          <p className="text-ink-muted">Sin evidencia: sube una foto del daño.</p>
        )}
        {operar && c.estado !== 'Cerrado' && (
          <div>
            <label className="inline-flex min-h-9 cursor-pointer items-center gap-2 rounded-md border border-line px-3 text-sm font-medium focus-within:ring-2 focus-within:ring-ring">
              <Camera className="size-4" aria-hidden="true" />
              {c.tieneEvidencia ? 'Reemplazar foto' : 'Subir foto'}
              <input type="file" accept="image/jpeg,image/png" className="sr-only" aria-label="Foto de evidencia del caso" disabled={accion.isPending} onChange={(e) => void subirFoto(e.target.files?.[0])} />
            </label>
            <span className="ml-2 text-xs text-ink-muted">JPEG o PNG; se reduce a 1280 px.</span>
          </div>
        )}
      </section>

      <section aria-label="Dictamen de Calidad" className="flex flex-col gap-2">
        <h3 className="text-md font-semibold">Dictamen de Calidad</h3>
        {c.dictamen ? (
          <div className="flex flex-col gap-1">
            <p><Badge variant="info">{dictamenTexto[c.dictamen]}</Badge></p>
            {c.observacionDictamen && <p>{c.observacionDictamen}</p>}
            {c.pedidoRefabricacionAw && <p className="text-xs">Pedido de refabricación en A+W: <span className="font-mono">{c.pedidoRefabricacionAw}</span></p>}
            <p className="text-xs text-ink-muted">{nombreUsuario(c.dictaminadoPorId)}{c.dictaminadoAt && <> · <DateTimeDisplay value={c.dictaminadoAt} variant="datetime" /></>}</p>
          </div>
        ) : puedeDictaminar ? (
          <DictamenForm pendiente={accion.isPending} onEnviar={(body) => ejecutar({ tipo: 'dictaminar', id: c.id, body }, 'Dictamen registrado').catch(() => undefined)} />
        ) : (
          <p className="text-ink-muted">Pendiente del dictamen de Calidad.</p>
        )}
      </section>

      {mostrarCorrectivo && (
        <section aria-label="Correctivo fiscal" className="flex flex-col gap-2">
          <h3 className="text-md font-semibold">Correctivo fiscal</h3>
          {c.correctivo ? (
            <p>
              {c.correctivo === 'NotaCredito' ? 'Nota de crédito' : 'Cancelación de factura'}
              {c.documento && (
                <> · <span className="font-mono">{c.documento.folio ?? 'sin folio'}</span> · {c.documento.estado}{' '}
                  {c.documento.pendiente && <Badge variant="warning">Pendiente</Badge>}</>
              )}
            </p>
          ) : (
            <>
              <p className="text-xs text-ink-muted">La factura {c.facturaFolio} ya se emitió: elige cómo se corrige.</p>
              <div className="flex flex-wrap items-start gap-3">
                {puedeNc && <Button variant="outline" disabled={accion.isPending} onClick={() => void ejecutar({ tipo: 'nota-credito', id: c.id }, 'Nota de crédito solicitada').catch(() => undefined)}>Emitir nota de crédito</Button>}
                {puedeCancelar && <CancelarFacturaForm pendiente={accion.isPending} onEnviar={(body) => ejecutar({ tipo: 'cancelar-factura', id: c.id, body }, 'Cancelación solicitada').catch(() => undefined)} />}
                {!puedeNc && !puedeCancelar && <p className="text-ink-muted">Sin permiso para emitir el correctivo.</p>}
              </div>
            </>
          )}
        </section>
      )}

      {c.estado === 'Cerrado' ? (
        c.cerradoAt && <p className="text-xs text-ink-muted">Cerrado <DateTimeDisplay value={c.cerradoAt} variant="datetime" />.</p>
      ) : (
        operar && c.estado === 'Dictaminado' && (
          <div><Button disabled={accion.isPending} onClick={() => void ejecutar({ tipo: 'cerrar', id: c.id }, 'Caso cerrado').catch(() => undefined)}>Cerrar caso</Button></div>
        )
      )}
    </article>
  );
}

function DictamenForm({ pendiente, onEnviar }: { pendiente: boolean; onEnviar: (b: { dictamen: DictamenCaso; observacion: string; pedidoRefabricacionAw?: string }) => void }) {
  const [dictamen, setDictamen] = useState<DictamenCaso | ''>('');
  const [observacion, setObservacion] = useState('');
  const [pedidoAw, setPedidoAw] = useState('');
  const lo = observacion.trim().length;
  const valido = dictamen !== '' && lo >= 5 && lo <= 500;
  const enviar = (e: FormEvent) => {
    e.preventDefault();
    if (!valido) return;
    onEnviar({ dictamen, observacion: observacion.trim(), ...(dictamen === 'Refabricacion' && pedidoAw.trim() ? { pedidoRefabricacionAw: pedidoAw.trim() } : {}) });
  };
  return (
    <form onSubmit={enviar} className="grid gap-3 sm:grid-cols-2">
      <div className="flex flex-col gap-1">
        <Label htmlFor="pt-dictamen">Dictamen</Label>
        <select id="pt-dictamen" className={selectClass} required value={dictamen} onChange={(e) => setDictamen(e.target.value as DictamenCaso | '')}>
          <option value="">Selecciona un dictamen</option>
          {(Object.keys(dictamenTexto) as DictamenCaso[]).map((d) => <option key={d} value={d}>{dictamenTexto[d]}</option>)}
        </select>
      </div>
      {dictamen === 'Refabricacion' && (
        <div className="flex flex-col gap-1">
          <Label htmlFor="pt-pedido-aw">Pedido de refabricación en A+W (opcional)</Label>
          <Input id="pt-pedido-aw" maxLength={50} value={pedidoAw} onChange={(e) => setPedidoAw(e.target.value)} />
          <p className="text-xs text-ink-muted">La refabricación se pide como pedido nuevo en A+W; aquí solo se guarda su número.</p>
        </div>
      )}
      <div className="flex flex-col gap-1 sm:col-span-2">
        <Label htmlFor="pt-observacion">Observación</Label>
        <Textarea id="pt-observacion" rows={3} maxLength={500} aria-invalid={lo > 0 && lo < 5} value={observacion} onChange={(e) => setObservacion(e.target.value)} />
        <p className="text-xs text-ink-muted">Entre 5 y 500 caracteres ({lo}).</p>
      </div>
      <div className="sm:col-span-2"><Button type="submit" disabled={!valido || pendiente}>Registrar dictamen</Button></div>
    </form>
  );
}

function CancelarFacturaForm({ pendiente, onEnviar }: { pendiente: boolean; onEnviar: (b: { motivoSat: '01' | '02' | '03' | '04'; uuidSustituto?: string }) => void }) {
  const [abierto, setAbierto] = useState(false);
  const [motivo, setMotivo] = useState<'01' | '02' | '03' | '04' | ''>('');
  const [uuid, setUuid] = useState('');
  const valido = motivo !== '' && (motivo !== '01' || UUID.test(uuid.trim()));
  if (!abierto) return <Button variant="outline" onClick={() => setAbierto(true)}>Cancelar factura</Button>;
  return (
    <form
      className="flex min-w-72 flex-col gap-2 rounded-md border border-line-divider p-3"
      onSubmit={(e) => { e.preventDefault(); if (valido) onEnviar({ motivoSat: motivo as '01', ...(motivo === '01' ? { uuidSustituto: uuid.trim() } : {}) }); }}
    >
      <div className="flex flex-col gap-1">
        <Label htmlFor="pt-motivo-sat">Motivo SAT</Label>
        <select id="pt-motivo-sat" className={selectClass} required value={motivo} onChange={(e) => setMotivo(e.target.value as '01')}>
          <option value="">Selecciona un motivo</option>
          {motivosSat.map((m) => <option key={m.v} value={m.v}>{m.t}</option>)}
        </select>
      </div>
      {motivo === '01' && (
        <div className="flex flex-col gap-1">
          <Label htmlFor="pt-uuid-sustituto">UUID de la factura que la sustituye</Label>
          <Input id="pt-uuid-sustituto" className="font-mono text-xs" aria-invalid={uuid !== '' && !UUID.test(uuid.trim())} value={uuid} onChange={(e) => setUuid(e.target.value)} />
        </div>
      )}
      <div className="flex gap-2">
        <Button type="submit" size="sm" disabled={!valido || pendiente}>Solicitar cancelación</Button>
        <Button type="button" size="sm" variant="outline" onClick={() => setAbierto(false)}>Cerrar</Button>
      </div>
    </form>
  );
}
