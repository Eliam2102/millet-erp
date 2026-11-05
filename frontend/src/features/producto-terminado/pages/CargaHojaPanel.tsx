import { useEffect, useRef, useState, type FormEvent } from 'react';
import { ScanLine } from 'lucide-react';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { mensajeErrorAccion } from '@/lib/api/error';
import { useCargaHoja } from '../api/hooks';
import type { AlbaranDto, HojaSalidaDto } from '../api/types';

/** Carga física de la ruta, pieza por pieza (PT5.4): pensada para celular y tablet (objetivos de 44 px, una columna en móvil). */
export function CargaHojaPanel({ hoja, sucursal, onEmitir, emitiendo }: { hoja: HojaSalidaDto; sucursal: string; onEmitir: () => void; emitiendo: boolean }) {
  const carga = useCargaHoja();
  const [etiqueta, setEtiqueta] = useState('');
  const [aviso, setAviso] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const entrada = useRef<HTMLInputElement>(null);

  useEffect(() => entrada.current?.focus(), [hoja.id]);

  const ejecutar = (accion: 'cargar' | 'descargar' | 'rota', et: string, ok: string) => {
    setError(null);
    setAviso(null);
    return carga
      .mutateAsync({ id: hoja.id, accion, etiqueta: et.trim() })
      .then(() => { setAviso(ok); return true; })
      .catch((e) => { setError(mensajeErrorAccion(e)); return false; })
      .finally(() => entrada.current?.focus());
  };
  const escanear = (e: FormEvent) => {
    e.preventDefault();
    const et = etiqueta.trim();
    if (!et) return;
    void ejecutar('cargar', et, `Cargada ${et}.`).then((ok) => ok && setEtiqueta(''));
  };

  const cargadas = hoja.albaranes.reduce((n, a) => n + a.total, 0);
  const esperadas = hoja.albaranes.reduce((n, a) => n + (a.esperadas ?? 0), 0);

  return (
    <section aria-label="Carga de la ruta" className="flex flex-col gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-base font-medium">Carga de la ruta {hoja.folio}</h2>
          <p className="text-ink-muted">{sucursal} a {hoja.destino}</p>
        </div>
        <p className="text-base font-medium tabular-nums" aria-label="Piezas cargadas">{cargadas} de {esperadas} piezas cargadas</p>
      </div>

      <form onSubmit={escanear} className="flex flex-col gap-2 sm:flex-row">
        <label className="sr-only" htmlFor="escaneo-carga">Etiqueta de la pieza</label>
        <Input
          id="escaneo-carga"
          ref={entrada}
          className="h-12 flex-1 font-mono text-base"
          placeholder="Escanea o escribe la etiqueta de la pieza"
          autoComplete="off"
          autoCapitalize="characters"
          value={etiqueta}
          onChange={(e) => setEtiqueta(e.target.value)}
        />
        <Button type="submit" size="lg" className="min-h-11" disabled={!etiqueta.trim() || carga.isPending}>
          <ScanLine className="size-4" aria-hidden="true" /> Cargar pieza
        </Button>
      </form>
      {error && (
        <AvisoEnLinea tono="error" titulo="No se cargó la pieza">
          {error}
        </AvisoEnLinea>
      )}
      {aviso && <p role="status" className="rounded-md bg-success-bg p-3 text-success-fg">{aviso}</p>}

      <div className="grid gap-3 lg:grid-cols-2">
        {hoja.albaranes.map((a) => (
          <PedidoCarga key={a.id} albaran={a} ocupado={carga.isPending} onAccion={ejecutar} />
        ))}
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-line-divider pt-3">
        <p className="text-ink-muted">El albarán de cada pedido lleva solo lo cargado; lo que falte queda pendiente.</p>
        <Button size="lg" className="min-h-11 max-sm:w-full" disabled={cargadas === 0 || emitiendo || carga.isPending} onClick={onEmitir}>Emitir lo cargado</Button>
      </div>
    </section>
  );
}

type Accion = (accion: 'cargar' | 'descargar' | 'rota', etiqueta: string, ok: string) => Promise<boolean>;

function PedidoCarga({ albaran: a, ocupado, onAccion }: { albaran: AlbaranDto; ocupado: boolean; onAccion: Accion }) {
  const pendientes = Math.max((a.esperadas ?? 0) - a.total, 0);
  return (
    <article aria-label={`Pedido ${a.numeroPedido}`} className="flex flex-col gap-2 rounded-md border border-line-divider p-3">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="font-medium">Entrega {a.secuencia} · Pedido {a.numeroPedido}</h3>
          <p className="text-xs text-ink-muted">{a.cliente}{a.destino ? ` · ${a.destino}` : ''}</p>
        </div>
        <Badge variant={pendientes === 0 && a.total > 0 ? 'success' : 'info'}>{a.total} de {a.esperadas ?? 0} cargadas</Badge>
      </header>
      <ul className="flex flex-col">
        {a.etiquetas.map((et) => (
          <li key={et} className="flex min-h-11 items-center justify-between gap-2 py-1 border-b border-line-divider last:border-0">
            <span className="font-mono text-xs">{et}</span>
            <span className="flex items-center gap-2">
              <Badge variant="success">Cargada</Badge>
              <Button variant="outline" size="sm" className="min-h-11 min-w-11" disabled={ocupado} onClick={() => void onAccion('descargar', et, `Quitada ${et} del camión.`)}>Quitar</Button>
              <Button variant="outline" size="sm" className="min-h-11 min-w-11" disabled={ocupado} onClick={() => void onAccion('rota', et, `${et} marcada como rota: queda pendiente de reposición.`)}>Rota</Button>
            </span>
          </li>
        ))}
        {(a.disponibles ?? []).map((et) => (
          <li key={et} className="flex min-h-11 items-center justify-between gap-2 py-1 border-b border-line-divider last:border-0">
            <span className="font-mono text-xs">{et}</span>
            <span className="flex items-center gap-2">
              <Button size="sm" className="min-h-11 min-w-11" disabled={ocupado} onClick={() => void onAccion('cargar', et, `Cargada ${et}.`)}>Cargar</Button>
              <Button variant="outline" size="sm" className="min-h-11 min-w-11" disabled={ocupado} onClick={() => void onAccion('rota', et, `${et} marcada como rota: queda pendiente de reposición.`)}>Rota</Button>
            </span>
          </li>
        ))}
        {(a.rotas ?? []).map((et) => (
          <li key={et} className="flex min-h-11 items-center justify-between gap-2 py-1 border-b border-line-divider last:border-0">
            <span className="font-mono text-xs">{et}</span>
            <Badge variant="warning">Rota · pendiente de reposición</Badge>
          </li>
        ))}
      </ul>
    </article>
  );
}
