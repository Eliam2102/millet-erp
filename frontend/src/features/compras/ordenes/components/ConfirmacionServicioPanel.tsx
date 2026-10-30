import { useState } from 'react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Textarea } from '@/components/ui/textarea';
import { DateTimeDisplay, Field } from '@/components/erp';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { EstadoOrdenCompra, type OrdenCompraDetalleResponse } from '../api/types';
import { useAccionServicioOc } from '../api/useConfirmarServicioOc';

type Modo = 'confirmar' | 'deshacer' | 'no-conforme';

const TITULO: Record<Modo, string> = {
  confirmar: 'Confirmar servicio',
  deshacer: 'Deshacer confirmación',
  'no-conforme': 'No quedé conforme',
};

function mensajeError(error: unknown) {
  return esApiError(error)
    ? (error.problem.detail ?? error.problem.title)
    : 'No se pudo guardar. Vuelve a intentarlo.';
}

/**
 * 86e3tacbn: «servicio realizado y recibido a satisfacción», una confirmación por OC que cubre todas sus partidas
 * de servicio. Sin ella CxP no libera a pago la factura. Solo el departamento solicitante puede marcarla
 * (el backend lo valida); no se puede deshacer si la OC ya tiene una factura pagada.
 */
export function ConfirmacionServicioPanel({ oc }: { oc: OrdenCompraDetalleResponse }) {
  const puede = useHasPermission(PermisosCanonicos.ComprasOrdenesConfirmarServicio);
  const mutation = useAccionServicioOc();
  const [modo, setModo] = useState<Modo | null>(null);
  const [texto, setTexto] = useState('');
  const [error, setError] = useState<string | null>(null);

  const tieneServicio = oc.lineas.some((l) => l.esServicio);
  const confirmada = !!oc.servicioConfirmadoEn;
  const autorizada = oc.estado === EstadoOrdenCompra.Autorizada;
  if (!tieneServicio || (!autorizada && !confirmada)) return null;

  const conPago = (oc.montoPagado ?? 0) > 0;
  const editable = puede && autorizada;

  function abrir(m: Modo | null) {
    setModo(m);
    setTexto('');
    setError(null);
  }

  async function enviar(e: React.FormEvent) {
    e.preventDefault();
    if (!modo) return;
    if (modo !== 'confirmar' && !texto.trim()) {
      setError(modo === 'deshacer' ? 'Indica el motivo para deshacer la confirmación.' : 'Indica por qué no quedaste conforme.');
      return;
    }
    try {
      await mutation.mutateAsync({
        ordenCompraId: oc.id,
        idempotencyKey: crypto.randomUUID(),
        ...(modo === 'confirmar'
          ? { accion: 'confirmar' as const, texto: texto.trim() || null }
          : { accion: modo, texto: texto.trim() }),
      });
      toast.success(
        modo === 'confirmar'
          ? 'Servicio confirmado.'
          : modo === 'deshacer'
            ? 'Confirmación deshecha.'
            : 'Aviso enviado a Compras.',
      );
      abrir(null);
    } catch (err) {
      setError(mensajeError(err));
    }
  }

  return (
    <section
      aria-label="Conformidad del servicio"
      className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-md font-semibold">Conformidad del servicio</h2>
        {confirmada ? (
          <Badge variant="success">Confirmado</Badge>
        ) : (
          <Badge variant="warning">Pendiente: la factura no pasa a pago</Badge>
        )}
      </div>

      <div className="flex items-center gap-2">
        <Checkbox
          id="servicio-conforme"
          checked={confirmada}
          disabled={!editable || mutation.isPending || (confirmada && conPago)}
          onCheckedChange={(v) => abrir(v === true ? 'confirmar' : 'deshacer')}
        />
        <label htmlFor="servicio-conforme" className="text-sm">
          Servicio realizado y recibido a satisfacción
        </label>
      </div>
      {!puede && !confirmada && (
        <p className="text-xs text-ink-muted">Solo el área que pidió el servicio puede confirmarlo.</p>
      )}
      {confirmada && conPago && (
        <p className="text-xs text-ink-muted">
          No se puede deshacer: la OC ya tiene una factura pagada.
        </p>
      )}
      {confirmada && (
        <p className="text-sm text-ink-secondary">
          Confirmado por {oc.servicioConfirmadoPorNombre ?? 'usuario no disponible'} el{' '}
          <DateTimeDisplay value={oc.servicioConfirmadoEn!} variant="datetime" />
          {oc.servicioConfirmadoComentario ? `: ${oc.servicioConfirmadoComentario}` : '.'}
        </p>
      )}
      {!confirmada && oc.servicioNoConformeEn && (
        <p role="status" className="text-sm text-danger-fg">
          No conforme ({oc.servicioNoConformePorNombre ?? 'usuario no disponible'},{' '}
          <DateTimeDisplay value={oc.servicioNoConformeEn} variant="datetime" />
          ): {oc.servicioNoConformeMotivo}
        </p>
      )}
      {!confirmada && oc.servicioConfirmacionRevocadaEn && (
        <p className="text-xs text-ink-muted">
          Última confirmación deshecha el <DateTimeDisplay value={oc.servicioConfirmacionRevocadaEn} variant="datetime" />:{' '}
          {oc.servicioConfirmacionRevocadaMotivo}
        </p>
      )}

      {editable && !confirmada && modo === null && (
        <Button variant="secondary" onClick={() => abrir('no-conforme')}>
          No quedé conforme
        </Button>
      )}

      {modo && (
        <form
          onSubmit={enviar}
          noValidate
          aria-label={TITULO[modo]}
          className="space-y-3 rounded-md border border-dashed border-brand/40 bg-brand/5 p-3"
        >
          <Field
            label={modo === 'confirmar' ? 'Comentario (opcional)' : modo === 'deshacer' ? 'Motivo para deshacer' : 'Motivo'}
            required={modo !== 'confirmar'}
          >
            <Textarea maxLength={500} value={texto} onChange={(e) => setTexto(e.target.value)} />
          </Field>
          {modo === 'no-conforme' && (
            <p className="text-xs text-ink-muted">Se avisa a Compras con este motivo.</p>
          )}
          {error && (
            <p role="alert" className="text-sm text-danger-fg">
              {error}
            </p>
          )}
          <div className="flex justify-end gap-2">
            <Button type="button" variant="ghost" onClick={() => abrir(null)} disabled={mutation.isPending}>
              Cancelar
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {TITULO[modo]}
            </Button>
          </div>
        </form>
      )}
    </section>
  );
}
