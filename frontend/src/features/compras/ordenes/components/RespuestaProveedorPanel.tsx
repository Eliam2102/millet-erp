import { useState } from 'react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DateTimeDisplay } from '@/components/erp';
import { useUsuarios, mapById } from '@/features/catalogos/api';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import {
  EstadoOrdenCompra,
  TipoRespuestaProveedorOc,
  type OrdenCompraDetalleResponse,
} from '../api/types';
import { useRegistrarRespuestaProveedor } from '../api/useRegistrarRespuestaProveedor';
import { EstadoComercialProveedorBadge } from './EstadoComercialProveedorBadge';
import { RegistrarRespuestaProveedorSheet } from './RegistrarRespuestaProveedorSheet';

/** F1-COM-11: panel «Respuesta del proveedor» del detalle de OC (estado, compromiso vigente, historial). */
export function RespuestaProveedorPanel({ oc }: { oc: OrdenCompraDetalleResponse }) {
  const usuarios = useUsuarios();
  const nombres = mapById(usuarios.data?.items);
  const puedeRegistrar = useHasPermission(PermisosCanonicos.ComprasOrdenesRegistrarRespuestaProveedor);
  const mutation = useRegistrarRespuestaProveedor();
  const [abierto, setAbierto] = useState(false);
  const respuestas = oc.respuestasProveedor ?? [];
  const autorizada = oc.estado === EstadoOrdenCompra.Autorizada;
  if (!autorizada && respuestas.length === 0) return null;
  const nombre = (id: string) => nombres.get(id)?.nombre ?? id;
  const posicion = (lineaId: string) => oc.lineas.find((l) => l.id === lineaId)?.posicion ?? '—';

  return (
    <section aria-label="Respuesta del proveedor" className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="text-md font-semibold">Respuesta del proveedor</h2>
      <div className="flex flex-wrap items-center gap-3 text-sm">
        <EstadoComercialProveedorBadge estado={oc.estadoComercialProveedor ?? 0} />
        {oc.fechaCompromisoProveedor && (
          <span>Compromiso vigente: <DateTimeDisplay value={oc.fechaCompromisoProveedor} /></span>
        )}
      </div>
      <p className="text-xs text-ink-muted">
        La respuesta no registra recepción ni cambia cantidades: recepción, factura y pago avanzan por separado.
      </p>
      {puedeRegistrar && (
        <Button
          variant="secondary"
          disabled={!autorizada}
          title={autorizada ? undefined : 'Solo se registra la respuesta en una orden autorizada.'}
          onClick={() => setAbierto(true)}
        >
          Registrar respuesta del proveedor
        </Button>
      )}
      {respuestas.map((r) => (
        <div key={r.id} className="space-y-1 border-t border-line-divider pt-3 text-sm">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-medium">Respuesta {r.numero}</span>
            <Badge variant={r.tipo === TipoRespuestaProveedorOc.Confirmacion ? 'info' : 'danger'}>
              {r.tipo === TipoRespuestaProveedorOc.Confirmacion ? 'Confirmación' : 'Rechazo'}
            </Badge>
            <Badge variant={r.vigente ? 'success' : 'neutral'}>{r.vigente ? 'Vigente' : 'Reemplazada'}</Badge>
            {r.tieneDiferencias && <Badge variant="warning">Con diferencias</Badge>}
          </div>
          <p className="text-ink-secondary">
            Fecha de respuesta: <DateTimeDisplay value={r.fechaRespuesta} /> · Registró {nombre(r.registradaPorId)}{' '}
            <DateTimeDisplay value={r.registradaEn} variant="datetime" />
          </p>
          {r.contactoProveedor && <p className="text-ink-secondary">Contacto: {r.contactoProveedor}</p>}
          <p className="text-ink-secondary">Evidencia: {r.evidencia}</p>
          {r.motivo && <p className="text-ink-secondary">{r.tipo === TipoRespuestaProveedorOc.Rechazo ? 'Motivo' : 'Comentario'}: {r.motivo}</p>}
          {r.compromisos.length > 0 && (
            <table className="w-full max-w-md text-left text-xs">
              <thead>
                <tr className="text-ink-muted">
                  <th scope="col" className="py-1 font-medium">Línea</th>
                  <th scope="col" className="py-1 text-right font-medium">Cantidad</th>
                  <th scope="col" className="py-1 pl-4 font-medium">Fecha prometida</th>
                </tr>
              </thead>
              <tbody>
                {r.compromisos.map((c) => (
                  <tr key={c.id}>
                    <td className="py-1">Línea {posicion(c.lineaOrdenCompraId)}</td>
                    <td className="py-1 text-right tabular-nums">{c.cantidad}</td>
                    <td className="py-1 pl-4"><DateTimeDisplay value={c.fechaPrometida} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          {/* P-11: evidencia en archivo (G1.2) */}
        </div>
      ))}
      <RegistrarRespuestaProveedorSheet
        oc={oc}
        open={abierto}
        onOpenChange={setAbierto}
        isPending={mutation.isPending}
        onSubmit={async (command) => {
          try {
            await mutation.mutateAsync({ ordenCompraId: oc.id, command, idempotencyKey: crypto.randomUUID() });
            toast.success('Respuesta del proveedor registrada.');
            setAbierto(false);
          } catch (error) {
            toast.error('No se pudo registrar la respuesta.', {
              description: esApiError(error) ? error.problem.detail ?? error.problem.title : 'Vuelve a intentarlo.',
            });
          }
        }}
      />
    </section>
  );
}
