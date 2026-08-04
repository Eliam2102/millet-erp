import { useState } from 'react';
import { toast } from 'sonner';
import { Loader2, RotateCcw, Trash2 } from 'lucide-react';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  useDescartarComprobante,
  useReintentarTimbrado,
} from '@/features/facturacion/api/useFacturas';

/**
 * Códigos de fallo AMBIGUOS: el PAC pudo haber timbrado sin que llegara la
 * respuesta — reintentar a ciegas duplicaría el CFDI ante el SAT. El
 * backend exige confirmación explícita (REINTENTO_REQUIERE_CONFIRMACION).
 */
const CODIGOS_AMBIGUOS = ['PAC_TIMEOUT', 'PAC_SIN_RESPUESTA', 'PAC_RESPUESTA_INCOMPLETA'];

/**
 * <c>&lt;TimbradoFallidoBanner/&gt;</c> — banner del detalle de un
 * comprobante en <c>TimbradoFallido</c> (cualquier tipo: factura, NC,
 * REPP, carta porte): muestra el error del PAC y ofrece el reintento de
 * timbrado (mismo folio interno — no re-emite). Para fallos ambiguos
 * exige marcar la verificación en FiscalAPI antes de habilitar el botón.
 */
export interface TimbradoFallidoBannerProps {
  comprobanteId: string;
  estado: string;
  errorCodigo: string | null;
  errorMensaje: string | null;
}

export function TimbradoFallidoBanner({
  comprobanteId,
  estado,
  errorCodigo,
  errorMensaje,
}: TimbradoFallidoBannerProps) {
  const puedeReintentar = useHasPermission(
    PermisosCanonicos.FacturacionComprobantesReintentarTimbrado,
  );
  const puedeDescartar = useHasPermission(
    PermisosCanonicos.FacturacionComprobantesDescartar,
  );
  const reintentar = useReintentarTimbrado();
  const descartar = useDescartarComprobante();
  const [confirmado, setConfirmado] = useState(false);
  const [confirmarDescarte, setConfirmarDescarte] = useState(false);

  if (estado !== 'TimbradoFallido') return null;

  const esAmbiguo =
    errorCodigo != null && CODIGOS_AMBIGUOS.includes(errorCodigo.toUpperCase());

  function handleReintentar() {
    // Key FRESCA por clic: reintentar timbrado debe volver a pegarle al PAC.
    // Una key estable devolvería la respuesta (fallida) cacheada del primer
    // intento y el PAC nunca se re-llamaría. La no-duplicación ante el SAT la
    // garantiza el backend (estado del comprobante + confirmarNoDuplicado en
    // fallos ambiguos), no la idempotency-key.
    reintentar.mutate(
      {
        id: comprobanteId,
        confirmarNoDuplicado: confirmado,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: (r) => {
          if (r.estado === 'Timbrado') {
            toast.success(`Timbrado exitoso — UUID ${r.uuid}.`);
          } else if (r.estado === 'TimbradoFallido') {
            toast.error(
              `El reintento volvió a fallar: ${r.timbradoErrorCodigo ?? ''} ${r.timbradoErrorMensaje ?? ''}`.trim(),
            );
          } else {
            toast.info(`El timbrado quedó ${r.estado}.`);
          }
        },
        onError: (error) => {
          toast.error(
            esApiError(error)
              ? error.problem.detail ?? error.problem.title
              : 'Error inesperado al reintentar el timbrado.',
          );
        },
      },
    );
  }

  function handleDescartar() {
    descartar.mutate(
      { id: comprobanteId, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: (r) => {
          toast.success(
            r.pedidoLiberadoId != null
              ? `Comprobante ${r.folio} descartado; el pedido quedó re-facturable.`
              : `Comprobante ${r.folio} descartado.`,
          );
        },
        onError: (error) => {
          toast.error(
            esApiError(error)
              ? error.problem.detail ?? error.problem.title
              : 'Error inesperado al descartar el comprobante.',
          );
        },
      },
    );
  }

  return (
    <div
      className="space-y-2 rounded-md border border-destructive/40 bg-destructive/5 px-3 py-2 text-sm"
      data-print="hidden"
    >
      <p>
        <span className="font-medium text-destructive">Timbrado fallido</span>
        {errorCodigo ? (
          <>
            {' — '}
            <code className="text-xs">{errorCodigo}</code>
          </>
        ) : null}
        {errorMensaje ? `: ${errorMensaje}` : null}
      </p>

      {puedeReintentar && (
        <>
          {esAmbiguo && (
            <div className="flex items-start gap-2">
              <Checkbox
                id="confirmarNoDuplicado"
                checked={confirmado}
                onCheckedChange={(v) => setConfirmado(v === true)}
              />
              <Label htmlFor="confirmarNoDuplicado" className="text-xs font-normal">
                Verifiqué en el dashboard de FiscalAPI que NO existe un timbre
                de este comprobante (el fallo fue ambiguo: reintentar sin
                verificar puede duplicar el CFDI ante el SAT).
              </Label>
            </div>
          )}

          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={handleReintentar}
            disabled={reintentar.isPending || (esAmbiguo && !confirmado)}
          >
            {reintentar.isPending ? (
              <Loader2 className="mr-1 h-3 w-3 animate-spin" />
            ) : (
              <RotateCcw className="mr-1 h-3 w-3" />
            )}
            Reintentar timbrado
          </Button>
        </>
      )}

      {puedeDescartar && (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="text-destructive"
          onClick={() => setConfirmarDescarte(true)}
          disabled={descartar.isPending || reintentar.isPending}
        >
          {descartar.isPending ? (
            <Loader2 className="mr-1 h-3 w-3 animate-spin" />
          ) : (
            <Trash2 className="mr-1 h-3 w-3" />
          )}
          Descartar
        </Button>
      )}

      <AlertDialog open={confirmarDescarte} onOpenChange={setConfirmarDescarte}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>¿Descartar este comprobante?</AlertDialogTitle>
            <AlertDialogDescription>
              Es definitivo: el comprobante ya no podrá reintentarse y su
              folio interno queda consumido (hueco consciente en el
              consecutivo). Si venía de un pedido, el pedido vuelve a quedar
              facturable. Úsalo cuando el documento ya no deba emitirse
              (pedido cancelado en origen, captura errónea de raíz) — para
              corregir datos y volver a timbrar usa &quot;Reintentar
              timbrado&quot;.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Conservar</AlertDialogCancel>
            <AlertDialogAction
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
              onClick={handleDescartar}
            >
              Descartar definitivamente
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
