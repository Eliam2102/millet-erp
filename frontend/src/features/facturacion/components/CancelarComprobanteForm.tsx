import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { MOTIVOS_CANCELACION } from '@/features/facturacion/api/types';
import { useSolicitarCancelacion } from '@/features/facturacion/api/useFacturas';
import { codigoSoporte, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useState } from 'react';
import { toast } from 'sonner';

/**
 * <c>&lt;CancelarComprobanteForm/&gt;</c> — form inline de cancelación SAT
 * 4.0 (FE-F5), extraído de <c>DetalleFactura</c> en ANT-PR2 (doc 13, 13-G):
 * el endpoint <c>POST /comprobantes/{id}/cancelar</c> es genérico, así que
 * el mismo form sirve para facturas de venta y facturas de anticipo. El
 * motivo 01 exige el UUID del comprobante sustituto.
 */
export function CancelarComprobanteForm({
  comprobanteId,
  onDone,
}: {
  comprobanteId: string;
  onDone: () => void;
}) {
  const [motivoSat, setMotivoSat] = useState('02');
  const [uuidSustituto, setUuidSustituto] = useState('');
  const idempotencyKey = useFormIdempotencyKey();
  const cancelar = useSolicitarCancelacion();
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
        onSuccess: (res) => {
          toast.success(`Cancelación solicitada (${res.estadoSolicitud}).`);
          onDone();
        },
        onError: (error) =>
          toast.error(
            esApiError(error) ? error.problem.title : 'No se pudo solicitar la cancelación.',
            {
              // El motivo real del SAT/PAC (p. ej. "no cancelable por tener
              // CFDI relacionados") viaja en detail — sin esto el usuario ve
              // un genérico y no puede accionar/reportar.
              description: esApiError(error)
                ? (error.problem.detail ?? codigoSoporte(error))
                : undefined,
            },
          ),
      },
    );
  }

  return (
    <FormSeccion titulo="Cancelación SAT 4.0">
      <FormCampo span={6}>
        <div>
          <Label className="text-xs">Motivo SAT *</Label>
          <select
            className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-sm"
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
            <Label className="text-xs">UUID sustituto *</Label>
            <Input
              className="font-mono text-xs"
              placeholder="00000000-…"
              value={uuidSustituto}
              onChange={(e) => setUuidSustituto(e.target.value)}
            />
          </div>
        )}
      </FormCampo>
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
