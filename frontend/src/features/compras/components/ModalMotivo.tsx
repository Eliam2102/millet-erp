import { useState } from 'react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { TextAreaField } from '@/components/erp/forms/TextAreaField';
import { MotivoRechazoSelector } from '@/features/compras/components/MotivoRechazoSelector';
import {
  MotivoRechazoAplicaA,
  type MotivoRechazoResponse,
} from '@/features/compras/api/types';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ModalMotivo/&gt;</c> — modal único para confirmar acciones
 * destructivas con motivo + texto libre opcional. Doc 05 §13.3 +
 * §11.3.
 *
 * <para><b>Tres variantes</b> gateadas por <c>aplicaA</c> del motivo:</para>
 * <list>
 *   <item><b>Rechazar</b> (<c>aplicaA=Rechazo</c>): título y botón
 *   rojo destructivo. Texto: "La RQ ya no podrá volver a Borrador".</item>
 *   <item><b>Eliminar</b> (<c>aplicaA=Eliminacion</c>): título y
 *   botón rojo. Texto: "La RQ se moverá a Eliminada".</item>
 *   <item><b>Cancelar</b> (<c>aplicaA=Cancelacion</c>): título
 *   naranja. Texto extra: "Las reservas se liberarán".</item>
 * </list>
 *
 * <para><b>Validación</b>:</para>
 * <list>
 *   <item>Motivo requerido — botón disabled si no hay selección.</item>
 *   <item>Si el motivo seleccionado tiene <c>permiteTextoLibre=true</c>,
 *   la textarea pasa a "Detalle (requerido)" y el botón sigue
 *   disabled hasta que haya texto.</item>
 * </list>
 *
 * <para>API: el caller pasa <c>onConfirm({ motivoId, motivoTexto })</c>
 * y maneja la mutation. El modal solo recolecta input — no acopla a un
 * hook particular, así puede usarse para Rechazar (UF3-PR1), Eliminar
 * RQ (UF4-PR1) y Cancelar (UF4-PR1) con la misma forma.</para>
 */
export type ModalMotivoVariante =
  | 'rechazar'
  | 'eliminar'
  | 'cancelar'
  | 'cerrar-manual';

export interface ModalMotivoConfirmArgs {
  motivoId: string;
  motivoTexto: string | null;
}

export interface ModalMotivoProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  variante: ModalMotivoVariante;
  /** Folio para incluirlo en el título (ej. <c>MID2026-000042</c>). */
  folio: string;
  onConfirm: (args: ModalMotivoConfirmArgs) => void;
  /** <c>true</c> mientras la mutation está en flight. */
  isPending?: boolean;
  /**
   * Aviso opcional (banner ámbar sobre el selector). Usado por el cierre
   * manual para advertir de OC en vuelo: "esta RQ tiene una OC en vuelo por
   * X piezas que llegarán como stock". Si es null/undefined no se renderiza.
   */
  aviso?: string | null;
}

const VARIANTE_CONFIG: Record<
  ModalMotivoVariante,
  {
    titulo: string;
    descripcion: string;
    extra?: string;
    aplicaA: MotivoRechazoAplicaA;
    accentClass: string;
    botonClass: string;
    botonTexto: string;
  }
> = {
  rechazar: {
    titulo: 'Rechazar requisición',
    descripcion:
      'Esta acción es definitiva. La RQ no podrá volver a Borrador ni re-enviarse a autorización.',
    aplicaA: MotivoRechazoAplicaA.Rechazo,
    accentClass: 'text-rose-700',
    botonClass: 'bg-destructive text-destructive-foreground hover:bg-destructive/90',
    botonTexto: 'Confirmar rechazo',
  },
  eliminar: {
    titulo: 'Eliminar requisición',
    descripcion:
      'Esta requisición se moverá al estado Eliminada y ya no podrá ser editada ni re-enviada a autorización.',
    aplicaA: MotivoRechazoAplicaA.Eliminacion,
    accentClass: 'text-rose-700',
    botonClass: 'bg-destructive text-destructive-foreground hover:bg-destructive/90',
    botonTexto: 'Confirmar eliminación',
  },
  cancelar: {
    titulo: 'Cancelar requisición',
    descripcion: 'Las reservas de stock se liberarán y cualquier OC borrador asociada se cancelará también.',
    extra: 'La RQ pasará al estado Cancelada y no podrá reactivarse.',
    aplicaA: MotivoRechazoAplicaA.Cancelacion,
    accentClass: 'text-amber-700',
    botonClass: 'bg-amber-600 text-white hover:bg-amber-700',
    botonTexto: 'Confirmar cancelación',
  },
  'cerrar-manual': {
    titulo: 'Cerrar requisición',
    descripcion:
      'Cierre administrativo: el material no entregado queda disponible como stock libre. El estado terminal depende de lo ya entregado.',
    extra:
      'La RQ pasará a Cerrada sin surtir o Cerrada surtida parcialmente y no podrá reactivarse.',
    aplicaA: MotivoRechazoAplicaA.CierreManual,
    accentClass: 'text-zinc-700',
    botonClass: 'bg-zinc-700 text-white hover:bg-zinc-800',
    botonTexto: 'Confirmar cierre',
  },
};

export function ModalMotivo({
  open,
  onOpenChange,
  variante,
  folio,
  onConfirm,
  isPending = false,
  aviso = null,
}: ModalMotivoProps) {
  const config = VARIANTE_CONFIG[variante];

  const [motivoId, setMotivoId] = useState<string | null>(null);
  const [motivoActual, setMotivoActual] =
    useState<MotivoRechazoResponse | null>(null);
  const [motivoTexto, setMotivoTexto] = useState<string>('');

  // Reset al cerrar — patrón "derived state" oficial (React docs):
  // setState durante render con un prev tracker en lugar de useEffect.
  // <para>Cuando <c>open</c> transiciona <c>true→false</c>, limpia el
  // form para que la próxima apertura arranque vacía.</para>
  const [openTracker, setOpenTracker] = useState(open);
  if (open !== openTracker) {
    setOpenTracker(open);
    if (!open) {
      setMotivoId(null);
      setMotivoActual(null);
      setMotivoTexto('');
    }
  }

  const requiereTexto = motivoActual?.permiteTextoLibre === true;
  const textoOK = !requiereTexto || motivoTexto.trim().length > 0;
  const puedeConfirmar = motivoId != null && textoOK && !isPending;

  function handleConfirm() {
    if (!puedeConfirmar || motivoId == null) return;
    onConfirm({
      motivoId,
      motivoTexto: motivoTexto.trim().length > 0 ? motivoTexto.trim() : null,
    });
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className={cn('flex items-baseline gap-2', config.accentClass)}>
            <span>⚠</span>
            <span>
              {config.titulo} <span className="font-mono text-base">{folio}</span>
            </span>
          </DialogTitle>
          <DialogDescription>
            {config.descripcion}
            {config.extra && (
              <>
                {' '}
                <span className="block">{config.extra}</span>
              </>
            )}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3">
          {aviso && (
            <div
              role="alert"
              className="rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-800"
            >
              ⚠ {aviso}
            </div>
          )}
          <div className="space-y-1.5">
            <label
              htmlFor="motivo-selector"
              className="text-sm font-medium"
            >
              Motivo <span className="text-rose-600">*</span>
            </label>
            <MotivoRechazoSelector
              id="motivo-selector"
              aplicaA={config.aplicaA}
              value={motivoId}
              onChange={(id) => setMotivoId(id)}
              onMotivoChange={(m) => {
                setMotivoActual(m);
                if (m == null || !m.permiteTextoLibre) {
                  // Si el motivo no requiere texto, no limpiamos el ya
                  // tipeado (puede ser un comentario voluntario).
                }
              }}
            />
          </div>

          <div className="space-y-1.5">
            <label className="text-sm font-medium">
              Detalle{' '}
              {requiereTexto ? (
                <span className="text-rose-600">(requerido) *</span>
              ) : (
                <span className="text-muted-foreground">(opcional)</span>
              )}
            </label>
            <TextAreaField
              value={motivoTexto.length > 0 ? motivoTexto : null}
              onChange={(v) => setMotivoTexto(v ?? '')}
              maxLength={500}
              minRows={2}
            />
          </div>
        </div>

        <DialogFooter className="gap-2">
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={isPending}
          >
            Cancelar
          </Button>
          <Button
            type="button"
            onClick={handleConfirm}
            disabled={!puedeConfirmar}
            className={config.botonClass}
            title={
              motivoId == null
                ? 'Selecciona un motivo'
                : !textoOK
                  ? 'Este motivo requiere detalle adicional'
                  : undefined
            }
          >
            {isPending ? 'Procesando…' : config.botonTexto}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
