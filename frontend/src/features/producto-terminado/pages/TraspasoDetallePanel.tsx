import { useState } from 'react';
import { Printer } from 'lucide-react';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { EvidenciaUpload, type EvidenciaUploadValue } from '@/features/almacen/components/EvidenciaUpload';
import { errorUsuario as mensajeErrorAccion } from '@/lib/error-usuario';
import { EVIDENCIA_TRASPASO_URL, useAclararDiferencia, useClasificarDiferencias, useRecibirTraspaso, useTraspaso } from '../api/hooks';
import type { ClasificacionDiferencia, TraspasoPiezaDto } from '../api/types';

const clasificaciones: Record<ClasificacionDiferencia, string> = {
  Faltante: 'Faltante',
  DanadaEnTransito: 'Dañada en tránsito',
  EnCuarentena: 'En cuarentena',
};

const resoluciones = { RecibidaTarde: 'Recibida tarde', RegresadaAlOrigen: 'Regresada al origen', DadaPorPerdida: 'Dada por pérdida (en revisión de Calidad)' } as const;

interface Borrador {
  clasificacion: ClasificacionDiferencia | '';
  motivo: string;
  evidencia: EvidenciaUploadValue | null;
}

const claseSelect = 'h-9 rounded-md border border-line bg-surface-card px-2 text-sm text-ink';

function estadoPieza(p: TraspasoPiezaDto, preparado: boolean) {
  if (p.resolucion) return resoluciones[p.resolucion];
  if (p.recibida) return 'Recibida';
  return preparado ? 'Sin salir' : 'En tránsito';
}

/** Detalle de un traspaso: hoja de traslado imprimible, recepción por pieza y clasificación y aclaración de diferencias (M-249, M-350). */
export function TraspasoDetallePanel({ id, nombre, operar }: { id: string; nombre: (sucursalId: string) => string; operar: boolean }) {
  const detalle = useTraspaso(id);
  const recibir = useRecibirTraspaso();
  const clasificar = useClasificarDiferencias();
  const aclarar = useAclararDiferencia();
  const [marcadas, setMarcadas] = useState<string[]>([]);
  const [borradores, setBorradores] = useState<Record<string, Borrador>>({});
  const [error, setError] = useState<string | null>(null);
  const t = detalle.data;
  if (!t) return null;

  const preparado = t.estado === 'Preparado';
  const enRecepcion = operar && (t.estado === 'EnTransito' || t.estado === 'ConDiferencia');
  const pendientes = t.piezas.filter((p) => !p.recibida && !p.resolucion);
  const sinClasificar = pendientes.filter((p) => !p.clasificacion);
  const borrador = (piezaId: string): Borrador => borradores[piezaId] ?? { clasificacion: '', motivo: '', evidencia: null };
  const cambiar = (piezaId: string, parche: Partial<Borrador>) => setBorradores((b) => ({ ...b, [piezaId]: { ...borrador(piezaId), ...parche } }));
  const alternar = (piezaId: string) => setMarcadas((m) => (m.includes(piezaId) ? m.filter((x) => x !== piezaId) : [...m, piezaId]));
  const falla = (e: unknown) => setError(mensajeErrorAccion(e));

  // Una pieza queda lista para clasificar con su clasificación y motivo; la dañada en tránsito, además con su evidencia.
  const completo = (b: Borrador) => b.clasificacion !== '' && b.motivo.trim() !== '' && (b.clasificacion !== 'DanadaEnTransito' || b.evidencia !== null);
  const listas = sinClasificar.filter((p) => completo(borrador(p.piezaId)));

  const confirmarRecepcion = () => {
    setError(null);
    recibir.mutateAsync({ id: t.id, piezaIds: marcadas }).then(() => setMarcadas([])).catch(falla);
  };
  const clasificarListas = () => {
    setError(null);
    clasificar
      .mutateAsync({
        id: t.id,
        piezas: listas.map((p) => {
          const b = borrador(p.piezaId);
          return { piezaId: p.piezaId, clasificacion: b.clasificacion as ClasificacionDiferencia, motivo: b.motivo.trim(), evidencia: b.evidencia?.blobRef ?? null };
        }),
      })
      .then(() => setBorradores({}))
      .catch(falla);
  };

  return (
    <section aria-label="Hoja de traslado" className="flex flex-col gap-3 rounded-lg bg-surface-card p-4 shadow-card-flat print:shadow-none">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h2 className="text-md font-semibold">Hoja de traslado {t.folio}</h2>
          <p className="text-ink-muted">
            {nombre(t.sucursalOrigenId)} a {nombre(t.sucursalDestinoId)} · <DateTimeDisplay value={t.autorizadoAt ?? t.salidaAt} variant="datetime" />
            {t.cartaPorteFolio ? ` · Carta Porte ${t.cartaPorteFolio}` : t.foranea ? ' · Carta Porte al autorizar' : ''}
          </p>
          {preparado && <p className="text-ink-muted">Pendiente de autorización: las piezas siguen en el PT de origen.</p>}
        </div>
        <Button data-print="hidden" variant="outline" size="sm" onClick={() => window.print()}>
          <Printer className="size-4" aria-hidden="true" /> Imprimir hoja de traslado
        </Button>
      </div>
      {error && (
        <AvisoEnLinea tono="error" titulo="No se pudo completar la acción">
          {error}
        </AvisoEnLinea>
      )}
      <ul className="flex flex-col gap-2">
        {t.piezas.map((p) => (
          <li key={p.piezaId} className="flex flex-col gap-1">
            <div className="flex flex-wrap items-center gap-2">
              {enRecepcion && !p.recibida && !p.resolucion ? (
                <Checkbox aria-label={`Recibir ${p.etiqueta}`} checked={marcadas.includes(p.piezaId)} onCheckedChange={() => alternar(p.piezaId)} />
              ) : null}
              <span>{p.etiqueta}</span>
              <span className="text-ink-muted">Pedido {p.numeroPedido}</span>
              <span>{estadoPieza(p, preparado)}</span>
              {p.clasificacion && (
                <span className="text-ink-muted">
                  {clasificaciones[p.clasificacion]}: {p.motivo}
                  {p.evidencia ? ' · con evidencia' : ''}
                </span>
              )}
              {enRecepcion && p.clasificacion && !p.recibida && !p.resolucion && (
                <span data-print="hidden" className="flex gap-2">
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={aclarar.isPending}
                    onClick={() => { setError(null); aclarar.mutateAsync({ id: t.id, piezaId: p.piezaId, resolucion: 'RegresadaAlOrigen' }).catch(falla); }}
                  >
                    Regresar {p.etiqueta} al origen
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={aclarar.isPending}
                    onClick={() => { setError(null); aclarar.mutateAsync({ id: t.id, piezaId: p.piezaId, resolucion: 'DadaPorPerdida' }).catch(falla); }}
                  >
                    Dar {p.etiqueta} por perdida
                  </Button>
                </span>
              )}
            </div>
            {enRecepcion && !p.clasificacion && !p.recibida && !p.resolucion && (
              <div data-print="hidden" className="flex flex-wrap items-start gap-2 pl-6">
                <select
                  className={claseSelect}
                  aria-label={`Clasificación de ${p.etiqueta}`}
                  value={borrador(p.piezaId).clasificacion}
                  onChange={(e) => cambiar(p.piezaId, { clasificacion: e.target.value as ClasificacionDiferencia | '' })}
                >
                  <option value="">Clasificar diferencia…</option>
                  {Object.entries(clasificaciones).map(([valor, texto]) => <option key={valor} value={valor}>{texto}</option>)}
                </select>
                <Input
                  className="w-72"
                  aria-label={`Motivo de ${p.etiqueta}`}
                  placeholder="Motivo (obligatorio)"
                  value={borrador(p.piezaId).motivo}
                  onChange={(e) => cambiar(p.piezaId, { motivo: e.target.value })}
                />
                <div className="w-72">
                  <EvidenciaUpload endpoint={EVIDENCIA_TRASPASO_URL} value={borrador(p.piezaId).evidencia} onChange={(v) => cambiar(p.piezaId, { evidencia: v })} />
                  <p className="text-xs text-ink-muted">Evidencia: obligatoria si llegó dañada, opcional en un faltante.</p>
                </div>
              </div>
            )}
          </li>
        ))}
      </ul>
      {enRecepcion && pendientes.length > 0 && (
        <div data-print="hidden" className="flex flex-wrap gap-2">
          <Button disabled={marcadas.length === 0 || recibir.isPending} onClick={confirmarRecepcion}>Recibir seleccionadas</Button>
          {sinClasificar.length > 0 && (
            <Button variant="outline" disabled={listas.length === 0 || clasificar.isPending} onClick={clasificarListas}>Clasificar diferencias</Button>
          )}
        </div>
      )}
    </section>
  );
}
