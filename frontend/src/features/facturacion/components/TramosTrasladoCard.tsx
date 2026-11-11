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
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import type { ModalidadTramo, TramoTraslado } from '@/features/facturacion/api/types';
import {
  useCrearTramo,
  useRegistrarDocumentoTercero,
  useTramosPedido,
} from '@/features/facturacion/api/useTramos';
import { useNuevaCartaPorte } from '@/features/facturacion/components/nueva-carta-porte-context';
import { mensajeErrorAccion } from '@/lib/api/error';
import { Link } from '@tanstack/react-router';
import { Plus, Route as RouteIcon } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';

export const MODALIDADES_TRAMO: { value: ModalidadTramo; label: string }[] = [
  { value: 'VehiculoPropio', label: 'Vehículo propio (Millet emite Carta Porte)' },
  { value: 'TransportistaTercero', label: 'Transportista tercero (documento del tercero)' },
];

/** Texto del estado del tramo según su modalidad. */
export function textoEstadoTramo(t: Pick<TramoTraslado, 'estado' | 'cartaPorteFolio'>): string {
  if (t.estado === 'PorEmitir') return 'Carta Porte por emitir';
  if (t.estado === 'SinDocumentoTercero') return 'Falta el documento del tercero';
  if (t.estado === 'DocumentoTercero') return 'Documento del tercero registrado';
  return `Carta Porte ${t.cartaPorteFolio ?? ''} · ${t.estado.replace('CartaPorte', '')}`.trim();
}

/**
 * M-354 · Tramos del traslado del pedido, cada uno con su modalidad. El tramo de vehículo propio emite Carta Porte de Millet;
 * el de transportista tercero no la emite y registra el documento del tercero.
 */
export function TramosTrasladoCard({
  pedidoId,
  puedeEmitir,
}: {
  pedidoId: string;
  puedeEmitir: boolean;
}) {
  const tramos = useTramosPedido(pedidoId);
  const nuevaCartaPorte = useNuevaCartaPorte();
  const [agregando, setAgregando] = useState(false);
  const [documentoDe, setDocumentoDe] = useState<TramoTraslado | null>(null);

  if (tramos.isError) return null;
  const lista = tramos.data ?? [];

  return (
    <section
      aria-label="Tramos del traslado"
      data-print="hidden"
      className="space-y-2 rounded-lg bg-surface-card px-4 py-3 shadow-card-flat"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="flex items-center gap-1.5 text-sm font-semibold text-ink">
            <RouteIcon className="h-4 w-4" strokeWidth={1.8} /> Tramos del traslado
          </h2>
          <p className="text-xs text-ink-muted">
            Millet emite Carta Porte solo en vehículos propios; el tramo de un transportista tercero registra el documento
            del tercero.
          </p>
        </div>
        {puedeEmitir ? (
          <Button type="button" variant="outline" size="sm" onClick={() => setAgregando(true)}>
            <Plus className="mr-1.5 h-3.5 w-3.5" strokeWidth={1.8} /> Agregar tramo
          </Button>
        ) : null}
      </div>

      {tramos.isLoading ? (
        <div className="h-8 w-full animate-pulse rounded bg-surface-muted" />
      ) : lista.length === 0 ? (
        <p className="text-xs text-ink-muted">El pedido aún no tiene tramos registrados.</p>
      ) : (
        <ul className="divide-y divide-line">
          {lista.map((t) => (
            <li key={t.id} className="flex flex-wrap items-center justify-between gap-2 py-2 text-sm">
              <div className="space-y-0.5">
                <p className="font-medium text-ink">
                  {t.orden}. {t.origen} → {t.destino}
                </p>
                <p className="text-xs text-ink-muted">
                  {t.modalidad === 'VehiculoPropio' ? 'Vehículo propio' : `Transportista tercero${t.terceroNombre ? ` · ${t.terceroNombre}` : ''}`}
                  {' · '}
                  {textoEstadoTramo(t)}
                  {t.documentoFolio ? ` · folio ${t.documentoFolio}` : ''}
                </p>
              </div>
              <div className="flex items-center gap-2">
                {t.cartaPorteId ? (
                  <Button asChild variant="outline" size="sm">
                    <Link to="/facturacion/carta-porte/$id" params={{ id: t.cartaPorteId }}>
                      Ver Carta Porte
                    </Link>
                  </Button>
                ) : null}
                {puedeEmitir && t.modalidad === 'VehiculoPropio' && t.cartaPorteId == null ? (
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    aria-label={`Emitir Carta Porte del tramo ${t.orden}`}
                    onClick={() =>
                      nuevaCartaPorte.abrir({
                        tramoId: t.id,
                        pedidoFacturableId: t.pedidoFacturableId,
                        origen: t.origen,
                        destino: t.destino,
                        origenCodigoPostal: t.origenCodigoPostal,
                        origenEstado: t.origenEstado,
                        destinoCodigoPostal: t.destinoCodigoPostal,
                        destinoEstado: t.destinoEstado,
                        distanciaKm: t.distanciaKm,
                      })
                    }
                  >
                    Emitir Carta Porte
                  </Button>
                ) : null}
                {puedeEmitir && t.modalidad === 'TransportistaTercero' ? (
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    aria-label={`Registrar documento del tercero del tramo ${t.orden}`}
                    onClick={() => setDocumentoDe(t)}
                  >
                    {t.documentoFolio || t.documentoUuid ? 'Editar documento del tercero' : 'Registrar documento del tercero'}
                  </Button>
                ) : null}
              </div>
            </li>
          ))}
        </ul>
      )}

      <AgregarTramoDialog pedidoId={pedidoId} open={agregando} onOpenChange={setAgregando} />
      <DocumentoTerceroDialog pedidoId={pedidoId} tramo={documentoDe} onCerrar={() => setDocumentoDe(null)} />
    </section>
  );
}

function AgregarTramoDialog({
  pedidoId,
  open,
  onOpenChange,
}: {
  pedidoId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const crear = useCrearTramo();
  const [modalidad, setModalidad] = useState<ModalidadTramo>('VehiculoPropio');
  const [origen, setOrigen] = useState('');
  const [destino, setDestino] = useState('');
  const [error, setError] = useState<string | null>(null);

  function cerrar() {
    setModalidad('VehiculoPropio');
    setOrigen('');
    setDestino('');
    setError(null);
    crear.reset();
    onOpenChange(false);
  }

  function enviar() {
    setError(null);
    crear.mutate(
      {
        pedidoId,
        body: { modalidad, origen: origen.trim(), ...(destino.trim() ? { destino: destino.trim() } : {}) },
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Tramo agregado.');
          cerrar();
        },
        onError: (e) => setError(mensajeErrorAccion(e)),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && cerrar()}>
      <DialogContent size="md" closeLabel="Cerrar" className="rounded-2xl shadow-dialog">
        <DialogHeader>
          <DialogTitle className="text-xl">Agregar tramo</DialogTitle>
          <DialogDescription className="text-xs text-ink-muted">
            Sin destino se toma la dirección de entrega del pedido cuando está disponible.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-3">
          <div className="space-y-1.5">
            <Label htmlFor="tramo-modalidad">Modalidad</Label>
            <Select value={modalidad} onValueChange={(v) => setModalidad(v as ModalidadTramo)}>
              <SelectTrigger id="tramo-modalidad" aria-label="Modalidad" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {MODALIDADES_TRAMO.map((m) => (
                  <SelectItem key={m.value} value={m.value}>
                    {m.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="tramo-origen">Origen</Label>
            <Input id="tramo-origen" value={origen} maxLength={200} onChange={(e) => setOrigen(e.target.value)} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="tramo-destino">Destino (opcional)</Label>
            <Input id="tramo-destino" value={destino} maxLength={200} onChange={(e) => setDestino(e.target.value)} />
          </div>
          {error ? (
            <p role="alert" className="text-sm text-danger-fg">
              {error}
            </p>
          ) : null}
        </DialogBody>
        <DialogFooter className="border-t border-line pt-4">
          <Button type="button" variant="ghost" onClick={cerrar}>
            Cancelar
          </Button>
          <Button type="button" disabled={crear.isPending || origen.trim() === ''} onClick={enviar}>
            {crear.isPending ? 'Agregando…' : 'Agregar tramo'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function DocumentoTerceroDialog({
  pedidoId,
  tramo,
  onCerrar,
}: {
  pedidoId: string;
  tramo: TramoTraslado | null;
  onCerrar: () => void;
}) {
  const registrar = useRegistrarDocumentoTercero();
  const [nombre, setNombre] = useState('');
  const [rfc, setRfc] = useState('');
  const [tipo, setTipo] = useState('Carta Porte');
  const [folio, setFolio] = useState('');
  const [uuid, setUuid] = useState('');
  const [fecha, setFecha] = useState('');
  const [error, setError] = useState<string | null>(null);

  function cerrar() {
    setNombre('');
    setRfc('');
    setTipo('Carta Porte');
    setFolio('');
    setUuid('');
    setFecha('');
    setError(null);
    registrar.reset();
    onCerrar();
  }

  function enviar() {
    if (tramo == null) return;
    setError(null);
    registrar.mutate(
      {
        pedidoId,
        tramoId: tramo.id,
        body: {
          terceroNombre: nombre.trim(),
          documentoTipo: tipo.trim(),
          ...(rfc.trim() ? { terceroRfc: rfc.trim() } : {}),
          ...(folio.trim() ? { folio: folio.trim() } : {}),
          ...(uuid.trim() ? { uuid: uuid.trim() } : {}),
          ...(fecha ? { fecha } : {}),
        },
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Documento del tercero registrado.');
          cerrar();
        },
        onError: (e) => setError(mensajeErrorAccion(e)),
      },
    );
  }

  return (
    <Dialog open={tramo != null} onOpenChange={(o) => !o && cerrar()}>
      <DialogContent size="md" closeLabel="Cerrar" className="rounded-2xl shadow-dialog">
        {tramo != null && (
          <>
            <DialogHeader>
              <DialogTitle className="text-xl">Documento del transportista tercero</DialogTitle>
              <DialogDescription className="text-xs text-ink-muted">
                Tramo {tramo.orden}: {tramo.origen} → {tramo.destino}. Millet no emite Carta Porte en este tramo.
              </DialogDescription>
            </DialogHeader>
            <DialogBody className="space-y-3">
              <div className="space-y-1.5">
                <Label htmlFor="tercero-nombre">Transportista</Label>
                <Input id="tercero-nombre" value={nombre} maxLength={254} onChange={(e) => setNombre(e.target.value)} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="tercero-rfc">RFC del transportista (opcional)</Label>
                <Input id="tercero-rfc" value={rfc} maxLength={13} onChange={(e) => setRfc(e.target.value)} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="tercero-tipo">Tipo de documento</Label>
                <Input id="tercero-tipo" value={tipo} maxLength={40} onChange={(e) => setTipo(e.target.value)} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="tercero-folio">Folio</Label>
                <Input id="tercero-folio" value={folio} maxLength={60} onChange={(e) => setFolio(e.target.value)} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="tercero-uuid">UUID (opcional si hay folio)</Label>
                <Input id="tercero-uuid" value={uuid} maxLength={36} onChange={(e) => setUuid(e.target.value)} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="tercero-fecha">Fecha del documento (opcional)</Label>
                <Input id="tercero-fecha" type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
              </div>
              {error ? (
                <p role="alert" className="text-sm text-danger-fg">
                  {error}
                </p>
              ) : null}
            </DialogBody>
            <DialogFooter className="border-t border-line pt-4">
              <Button type="button" variant="ghost" onClick={cerrar}>
                Cancelar
              </Button>
              <Button
                type="button"
                disabled={registrar.isPending || nombre.trim() === '' || (folio.trim() === '' && uuid.trim() === '')}
                onClick={enviar}
              >
                {registrar.isPending ? 'Registrando…' : 'Registrar documento'}
              </Button>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}
