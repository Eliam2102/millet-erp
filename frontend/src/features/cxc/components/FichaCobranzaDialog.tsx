import { useState } from 'react';
import { FormGrid, FormCampo } from '@/components/erp';
import {
  Dialog,
  DialogContent,
  DialogBody,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { TextAreaField } from '@/components/erp/forms/TextAreaField';
import { CanalCobranza, ResultadoCobranza } from '@/features/cxc/api/types';
import { MOTIVOS_CIERRE_FICHA } from '@/features/cxc/api/useFichasCobranza';
import { ETIQUETA_CANAL_COBRANZA, ETIQUETA_RESULTADO_COBRANZA } from '@/features/cxc/lib/glosario';

/**
 * M-241 (CXC-GESTION): un diálogo para las acciones de la ficha de cobranza de una factura vencida — asignar responsable, registrar
 * gestión (una promesa fija la siguiente revisión), reprogramar, escalar a supervisor y corregir una gestión. Todas dejan rastro.
 */
export type ModoFicha = 'responsable' | 'gestion' | 'reprogramar' | 'escalar' | 'corregir' | 'cerrar';

export interface PersonaOpcion {
  id: string;
  nombre: string;
}

export interface DatosFicha {
  personaId?: string;
  personaNombre?: string;
  canal?: number;
  resultado?: number;
  nota?: string;
  monto?: number;
  fecha?: string;
  motivo?: string;
  motivoCierre?: number;
}

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  modo: ModoFicha;
  objetivo: string;
  personas: PersonaOpcion[];
  /** Solo en modo corregir: nota actual del evento. */
  notaActual?: string;
  isPending?: boolean;
  onConfirm: (datos: DatosFicha) => void;
}

const TEXTOS: Record<ModoFicha, { titulo: string; descripcion: string; accion: string }> = {
  responsable: {
    titulo: 'Asignar responsable',
    descripcion: 'Abre la ficha de la factura o cambia a la persona responsable. Queda el rastro de quién lo hizo.',
    accion: 'Asignar',
  },
  gestion: {
    titulo: 'Registrar gestión',
    descripcion: 'Una promesa de pago fija la siguiente revisión en la fecha comprometida.',
    accion: 'Registrar',
  },
  reprogramar: {
    titulo: 'Reprogramar revisión',
    descripcion: 'Cambia la fecha de la siguiente revisión. El motivo es obligatorio y queda el antes y el después.',
    accion: 'Reprogramar',
  },
  escalar: {
    titulo: 'Escalar a supervisor',
    descripcion: 'El supervisor pasa a ser el responsable de la ficha y queda el rastro de quién escaló y por qué.',
    accion: 'Escalar',
  },
  cerrar: {
    titulo: 'Cerrar ficha',
    descripcion: 'Cierra la ficha con su motivo. Si la factura aún tiene saldo, la ficha no cierra sin explicar qué pasó con él.',
    accion: 'Cerrar ficha',
  },
  corregir: {
    titulo: 'Corregir gestión',
    descripcion: 'La gestión original no se edita: se agrega una corrección con quién la hizo, por qué y la nota anterior.',
    accion: 'Corregir',
  },
};

export function FichaCobranzaDialog({ open, onOpenChange, modo, objetivo, personas, notaActual, isPending, onConfirm }: Props) {
  const t = TEXTOS[modo];
  const [personaId, setPersonaId] = useState('');
  const [canal, setCanal] = useState<number>(CanalCobranza.Llamada);
  const [resultado, setResultado] = useState<number>(ResultadoCobranza.SinRespuesta);
  const [nota, setNota] = useState(modo === 'corregir' ? (notaActual ?? '') : '');
  const [monto, setMonto] = useState('');
  const [fecha, setFecha] = useState('');
  const [motivo, setMotivo] = useState('');
  const [motivoCierre, setMotivoCierre] = useState<number>(MOTIVOS_CIERRE_FICHA[0].valor);

  const esPromesa = modo === 'gestion' && resultado === ResultadoCobranza.PromesaPago;
  const pideMotivo = modo !== 'gestion' && modo !== 'cerrar';
  const pidePersona = modo === 'responsable' || modo === 'escalar';
  const pideFecha = modo === 'reprogramar' || esPromesa;
  const montoNum = Number(monto);
  const valido =
    (!pidePersona || personaId !== '') &&
    (!pideMotivo || modo === 'responsable' || motivo.trim().length > 0) &&
    (modo === 'responsable' || modo === 'reprogramar' || modo === 'escalar' || modo === 'cerrar' || nota.trim().length > 0) &&
    (!pideFecha || fecha !== '') &&
    (!esPromesa || (monto.trim() !== '' && montoNum > 0));

  function handleOpenChange(next: boolean) {
    if (!next) {
      setMotivo('');
      setNota(modo === 'corregir' ? (notaActual ?? '') : '');
    }
    onOpenChange(next);
  }

  function confirmar() {
    const persona = personas.find((p) => p.id === personaId);
    onConfirm({
      personaId: pidePersona ? personaId : undefined,
      personaNombre: persona?.nombre,
      canal: modo === 'gestion' ? canal : undefined,
      resultado: modo === 'gestion' ? resultado : undefined,
      nota: modo === 'gestion' || modo === 'corregir' ? nota.trim() : undefined,
      monto: esPromesa ? montoNum : undefined,
      fecha: pideFecha ? fecha : undefined,
      motivo: motivo.trim() || undefined,
      motivoCierre: modo === 'cerrar' ? motivoCierre : undefined,
    });
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>{t.titulo}</DialogTitle>
          <DialogDescription>{t.descripcion}</DialogDescription>
        </DialogHeader>
        <DialogBody>
          <p className="mb-3 text-sm font-medium text-ink">{objetivo}</p>
          <FormGrid>
            {pidePersona && (
              <FormCampo span={12} className="space-y-1">
                <Label htmlFor="ficha-persona" className="text-xs">
                  {modo === 'escalar' ? 'Supervisor *' : 'Responsable *'}
                </Label>
                <select
                  id="ficha-persona"
                  value={personaId}
                  onChange={(e) => setPersonaId(e.target.value)}
                  className="w-full rounded-md border border-input bg-background px-2 py-1.5 text-sm"
                >
                  <option value="">Elige a la persona…</option>
                  {personas.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.nombre}
                    </option>
                  ))}
                </select>
              </FormCampo>
            )}
            {modo === 'gestion' && (
              <>
                <FormCampo span={6} className="space-y-1">
                  <Label htmlFor="ficha-canal" className="text-xs">Canal *</Label>
                  <select
                    id="ficha-canal"
                    value={canal}
                    onChange={(e) => setCanal(Number(e.target.value))}
                    className="w-full rounded-md border border-input bg-background px-2 py-1.5 text-sm"
                  >
                    {Object.values(CanalCobranza).map((c) => (
                      <option key={c} value={c}>{ETIQUETA_CANAL_COBRANZA[c]}</option>
                    ))}
                  </select>
                </FormCampo>
                <FormCampo span={6} className="space-y-1">
                  <Label htmlFor="ficha-resultado" className="text-xs">Resultado *</Label>
                  <select
                    id="ficha-resultado"
                    value={resultado}
                    onChange={(e) => setResultado(Number(e.target.value))}
                    className="w-full rounded-md border border-input bg-background px-2 py-1.5 text-sm"
                  >
                    {Object.values(ResultadoCobranza).map((r) => (
                      <option key={r} value={r}>{ETIQUETA_RESULTADO_COBRANZA[r]}</option>
                    ))}
                  </select>
                </FormCampo>
              </>
            )}
            {esPromesa && (
              <FormCampo span={12} className="space-y-1">
                <Label htmlFor="ficha-monto" className="text-xs">Monto comprometido *</Label>
                <Input id="ficha-monto" inputMode="decimal" value={monto} onChange={(e) => setMonto(e.target.value.replace(/[^\d.]/g, ''))} />
              </FormCampo>
            )}
            {pideFecha && (
              <FormCampo span={12} className="space-y-1">
                <Label htmlFor="ficha-fecha" className="text-xs">
                  {esPromesa ? 'Fecha comprometida (será la siguiente revisión) *' : 'Siguiente revisión *'}
                </Label>
                <Input id="ficha-fecha" type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
              </FormCampo>
            )}
            {(modo === 'gestion' || modo === 'corregir') && (
              <FormCampo span={12} className="space-y-1">
                <Label htmlFor="ficha-nota" className="text-xs">{modo === 'corregir' ? 'Nota corregida *' : 'Nota *'}</Label>
                <TextAreaField value={nota} onChange={(v) => setNota(v ?? '')} maxLength={2000} textareaProps={{ id: 'ficha-nota' }} />
              </FormCampo>
            )}
            {modo === 'cerrar' && (
              <>
                <FormCampo span={12} className="space-y-1">
                  <Label htmlFor="ficha-motivo-cierre" className="text-xs">Motivo del cierre *</Label>
                  <select
                    id="ficha-motivo-cierre"
                    value={motivoCierre}
                    onChange={(e) => setMotivoCierre(Number(e.target.value))}
                    className="w-full rounded-md border border-input bg-background px-2 py-1.5 text-sm"
                  >
                    {MOTIVOS_CIERRE_FICHA.map((m) => (
                      <option key={m.valor} value={m.valor}>{m.etiqueta}</option>
                    ))}
                  </select>
                </FormCampo>
                <FormCampo span={12} className="space-y-1">
                  <Label htmlFor="ficha-explicacion" className="text-xs">Explicación del saldo pendiente (obligatoria si queda saldo)</Label>
                  <TextAreaField value={motivo} onChange={(v) => setMotivo(v ?? '')} maxLength={2000} textareaProps={{ id: 'ficha-explicacion' }} />
                </FormCampo>
              </>
            )}
            {pideMotivo && (
              <FormCampo span={12} className="space-y-1">
                <Label htmlFor="ficha-motivo" className="text-xs">{modo === 'responsable' ? 'Motivo (obligatorio al reasignar)' : 'Motivo *'}</Label>
                <TextAreaField value={motivo} onChange={(v) => setMotivo(v ?? '')} maxLength={400} textareaProps={{ id: 'ficha-motivo' }} />
              </FormCampo>
            )}
          </FormGrid>
        </DialogBody>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={() => handleOpenChange(false)} disabled={isPending}>Cancelar</Button>
          <Button type="button" disabled={!valido || isPending} onClick={confirmar}>{isPending ? 'Guardando…' : t.accion}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
