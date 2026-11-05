import { useState } from 'react';
import { Camera } from 'lucide-react';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { mensajeErrorAccion } from '@/lib/api/error';
import { useConfirmarEntrega } from '../api/hooks';
import { reducirFoto } from '../lib/imagen';
import type { AlbaranDto, FormaCobroEntrega } from '../api/types';
import { FirmaPad } from './FirmaPad';

const FORMAS: FormaCobroEntrega[] = ['TPV', 'Efectivo', 'Transferencia', 'Cheque'];

/**
 * Confirmación de la entrega de un pedido desde el celular del chofer (PT5.6): quién recibe, qué piezas aceptó (las que no, se rechazan en sitio con su motivo),
 * firma o foto como evidencia y, si cobró contra entrega, el voucher. Una columna en móvil y objetivos de 44 px.
 */
export function EntregaPanel({ hojaId, albaran, onListo, onCancelar }: { hojaId: string; albaran: AlbaranDto; onListo: () => void; onCancelar: () => void }) {
  const confirmar = useConfirmarEntrega();
  const [receptor, setReceptor] = useState('');
  const [rechazadas, setRechazadas] = useState<string[]>([]);
  const [motivo, setMotivo] = useState('');
  const [firma, setFirma] = useState<string | null>(null);
  const [foto, setFoto] = useState<string | null>(null);
  const [cobra, setCobra] = useState(false);
  const [monto, setMonto] = useState('');
  const [forma, setForma] = useState<FormaCobroEntrega>('TPV');
  const [referencia, setReferencia] = useState('');
  const [error, setError] = useState<string | null>(null);

  const total = albaran.etiquetas.length;
  const recibidas = total - rechazadas.length;
  const alternar = (et: string) => setRechazadas((r) => (r.includes(et) ? r.filter((x) => x !== et) : [...r, et]));
  const faltas = [
    !receptor.trim() && 'quién recibe',
    !firma && !foto && 'la firma o una foto',
    rechazadas.length > 0 && !motivo.trim() && 'el motivo del rechazo',
    cobra && !(Number(monto) > 0) && 'el monto cobrado',
  ].filter(Boolean) as string[];
  const resultado = rechazadas.length === 0 ? 'Entrega completa' : recibidas === 0 ? 'Rechazo total en sitio' : `Entrega parcial: ${recibidas} de ${total}`;

  const tomarFoto = async (archivo: File | undefined) => {
    if (!archivo) return;
    try { setFoto(await reducirFoto(archivo)); } catch { setError('No se pudo leer la foto. Intenta de nuevo.'); }
  };
  const enviar = () => {
    setError(null);
    confirmar
      .mutateAsync({
        hojaId,
        albaranId: albaran.id,
        body: {
          receptor: receptor.trim(), rechazadas, motivoRechazo: rechazadas.length > 0 ? motivo.trim() : null, firma, foto,
          cobro: cobra && recibidas > 0 ? { monto: Number(monto), formaPago: forma, referencia: referencia.trim() || null } : null,
        },
      })
      .then(onListo)
      .catch((e) => setError(mensajeErrorAccion(e)));
  };

  return (
    <section aria-label={`Confirmar entrega del pedido ${albaran.numeroPedido}`} className="flex flex-col gap-4 rounded-md border border-line-divider p-3">
      <label className="flex flex-col gap-1 text-sm">
        Quién recibe
        <Input className="h-12 text-base" autoComplete="off" placeholder="Nombre de quien recibe" value={receptor} onChange={(e) => setReceptor(e.target.value)} />
      </label>

      <fieldset className="flex flex-col">
        <legend className="mb-1 text-sm font-medium">Piezas: marca las que el cliente rechaza en sitio</legend>
        {albaran.etiquetas.map((et) => (
          <label key={et} className="flex min-h-11 items-center gap-3 border-b border-line-divider py-1 last:border-0">
            <Checkbox aria-label={`Rechazar ${et}`} checked={rechazadas.includes(et)} onCheckedChange={() => alternar(et)} />
            <span className="font-mono text-xs">{et}</span>
            <span className="text-xs text-ink-muted">{rechazadas.includes(et) ? 'Rechazada' : 'Recibida'}</span>
          </label>
        ))}
      </fieldset>
      {rechazadas.length > 0 && (
        <label className="flex flex-col gap-1 text-sm">
          Motivo del rechazo
          <Textarea className="min-h-20" placeholder="Ej. vidrio con rayón, medida distinta a la pedida" value={motivo} onChange={(e) => setMotivo(e.target.value)} />
          <span className="text-xs text-ink-muted">Las piezas rechazadas regresan a revisión y no se cobran.</span>
        </label>
      )}

      <div className="flex flex-col gap-2">
        <h4 className="text-sm font-medium">Evidencia: firma o foto</h4>
        <FirmaPad valor={firma} onChange={setFirma} />
        <div className="flex flex-wrap items-center gap-3">
          <label className="inline-flex min-h-11 cursor-pointer items-center gap-2 rounded-md border border-line px-3 text-sm font-medium">
            <Camera className="size-4" aria-hidden="true" />
            {foto ? 'Cambiar foto' : 'Tomar foto'}
            <input type="file" accept="image/*" capture="environment" className="sr-only" aria-label="Foto de la entrega" onChange={(e) => void tomarFoto(e.target.files?.[0])} />
          </label>
          {foto && <img src={foto} alt="Foto de la entrega" className="h-16 rounded-md border border-line object-cover" />}
        </div>
      </div>

      <div className="flex flex-col gap-2">
        <label className="flex min-h-11 items-center gap-3 text-sm">
          <Checkbox aria-label="Cobré en la entrega" checked={cobra} onCheckedChange={(v) => setCobra(v === true)} />
          Cobré en la entrega (contra entrega)
        </label>
        {cobra && (
          <div className="grid gap-2 sm:grid-cols-3">
            <label className="flex flex-col gap-1 text-xs text-ink-muted">
              Monto cobrado
              <Input className="h-12 text-base" inputMode="decimal" placeholder="8500.00" value={monto} onChange={(e) => setMonto(e.target.value)} />
            </label>
            <label className="flex flex-col gap-1 text-xs text-ink-muted">
              Forma de cobro
              <select className="h-12 rounded-md border border-line bg-surface-card px-2 text-base text-ink" value={forma} onChange={(e) => setForma(e.target.value as FormaCobroEntrega)}>
                {FORMAS.map((f) => <option key={f} value={f}>{f}</option>)}
              </select>
            </label>
            <label className="flex flex-col gap-1 text-xs text-ink-muted">
              Folio del voucher
              <Input className="h-12 text-base" placeholder="Folio de la terminal" value={referencia} onChange={(e) => setReferencia(e.target.value)} />
            </label>
          </div>
        )}
      </div>

      {error && <AvisoEnLinea tono="error" titulo="No se confirmó la entrega">{error}</AvisoEnLinea>}
      {faltas.length > 0 && <p role="note" className="text-xs text-ink-muted">Falta: {faltas.join(', ')}.</p>}
      <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
        <Button type="button" variant="outline" size="lg" className="min-h-11" onClick={onCancelar}>Cancelar</Button>
        <Button type="button" size="lg" className="min-h-11" disabled={faltas.length > 0 || confirmar.isPending} onClick={enviar}>Confirmar entrega · {resultado}</Button>
      </div>
    </section>
  );
}
