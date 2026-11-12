import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { MOTIVOS_CANCELACION } from '@/features/facturacion/api/types';
import { useAceptacionCancelacion, useSolicitarCancelacion } from '@/features/facturacion/api/useFacturas';
import { GUIA_MOTIVO_CANCELACION, textoAceptacion } from '@/features/facturacion/lib/guia-motivo-cancelacion';
import { useFormIdempotencyKey } from '@/lib/api';
import { useState } from 'react';
import { toast } from 'sonner';

/**
 * <c>&lt;CancelarComprobanteForm/&gt;</c> — form inline de cancelación SAT
 * 4.0 (FE-F5), extraído de <c>DetalleFactura</c> en ANT-PR2 (doc 13, 13-G):
 * el endpoint <c>POST /comprobantes/{id}/cancelar</c> es genérico, así que
 * el mismo form sirve para facturas de venta y facturas de anticipo. El
 * motivo 01 exige el UUID del comprobante sustituto. F6.5: la solicitud queda
 * pendiente de la autorización del Contador General.
 */
export function CancelarComprobanteForm({
  comprobanteId,
  onDone,
  sugerirSustituir = false,
}: {
  comprobanteId: string;
  onDone: () => void;
  /** FAC-08b: en facturas de venta, el motivo 01 se arma solo con «Sustituir». */
  sugerirSustituir?: boolean;
}) {
  const [motivoSat, setMotivoSat] = useState('02');
  const [uuidSustituto, setUuidSustituto] = useState('');
  const idempotencyKey = useFormIdempotencyKey();
  const cancelar = useSolicitarCancelacion();
  const aceptacion = useAceptacionCancelacion(comprobanteId);
  const guia = GUIA_MOTIVO_CANCELACION[motivoSat];
  const requiereSustituto =
    MOTIVOS_CANCELACION.find((m) => m.value === motivoSat)?.requiereSustituto ?? false;

  function enviar() {
    if (requiereSustituto && uuidSustituto.trim() === '') {
      toast.error('El motivo 01 exige el UUID del comprobante sustituto.');
      return;
    }
    cancelar.mutate(
      {
        id: comprobanteId,
        motivoSat,
        uuidSustituto: requiereSustituto ? uuidSustituto.trim() : null,
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Cancelación solicitada: queda pendiente de la autorización del Contador General.');
          onDone();
        },
        onError: () => toast.error('No se pudo solicitar la cancelación. Revisa el motivo, el comprobante sustituto y tu conexión antes de volver a solicitarla.'),
      },
    );
  }

  return (
    <FormSeccion titulo="Cancelación SAT 4.0">
      <FormCampo span={6}>
        <div>
          <Label htmlFor="cancelacion-motivo-sat" className="text-xs">Motivo SAT *</Label>
          <select
            id="cancelacion-motivo-sat"
            className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-card-flat"
            value={motivoSat}
            onChange={(e) => setMotivoSat(e.target.value)}
          >
            {MOTIVOS_CANCELACION.map((m) => (
              <option key={m.value} value={m.value}>
                {m.label}
              </option>
            ))}
          </select>
        </div>
      </FormCampo>
      <FormCampo span={4}>
        {requiereSustituto && (
          <div>
            <Label htmlFor="cancelacion-uuid-sustituto" className="text-xs">UUID sustituto *</Label>
            <Input
              id="cancelacion-uuid-sustituto"
              className="font-mono text-xs"
              placeholder="00000000-…"
              value={uuidSustituto}
              onChange={(e) => setUuidSustituto(e.target.value)}
            />
          </div>
        )}
      </FormCampo>
      {requiereSustituto && sugerirSustituir && (
        <FormCampo span={12}>
          <p role="note" className="rounded-md bg-info-bg px-3 py-2 text-sm text-info-fg">
            Para sustituir esta factura usa «Sustituir»: el ERP emite la nueva con relación 04 y arma esta
            solicitud con su UUID. Captura el UUID a mano solo si la sustituta se emitió fuera del ERP.
          </p>
        </FormCampo>
      )}
      {guia && (
        <FormCampo span={12}>
          <p role="note" className="rounded-md bg-info-bg px-3 py-2 text-sm text-info-fg">
            {guia.texto}
            {aceptacion.data && <> {textoAceptacion(aceptacion.data.requiereAceptacion, aceptacion.data.razonesSinAceptacion)}</>}
          </p>
        </FormCampo>
      )}
      <FormCampo span={12}>
        <div className="sm:col-span-3 flex justify-end gap-2">
          <Button variant="ghost" size="sm" onClick={onDone} disabled={cancelar.isPending}>
            Cerrar
          </Button>
          <Button variant="destructive" size="sm" onClick={enviar} disabled={cancelar.isPending}>
            {cancelar.isPending ? 'Solicitando…' : 'Solicitar cancelación'}
          </Button>
        </div>
      </FormCampo>
    </FormSeccion>
  );
}
