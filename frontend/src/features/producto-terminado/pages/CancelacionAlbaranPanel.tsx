import { useState } from 'react';
import { toast } from 'sonner';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { MotivoDialog } from '@/components/erp/feedback/MotivoDialog';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useCancelacionAlbaran } from '../api/hooks';
import type { AlbaranDto } from '../api/types';
import { mensajeApi } from '../lib/errores';
import { useNombreUsuario } from '../lib/usuarios';

type Paso = 'solicitar' | 'autorizar' | 'rechazar';

/**
 * M-247: cancelación de un albarán de una hoja Emitida, en dos pasos y por dos personas: una la pide con motivo y otra con permiso
 * `cancelar-emitida` la autoriza o la rechaza. Con la entrega ya confirmada no se cancela: se corrige la entrega o se abre un caso de daño.
 */
export function CancelacionAlbaranPanel({ hojaId, albaran, operar }: { hojaId: string; albaran: AlbaranDto; operar: boolean }) {
  const mutar = useCancelacionAlbaran();
  const puedeResolver = useHasPermission(P.ProductoTerminadoSalidaCancelarEmitida);
  const nombreUsuario = useNombreUsuario();
  const [paso, setPaso] = useState<Paso | null>(null);
  const [error, setError] = useState<string | null>(null);
  const c = albaran.cancelacion;

  if (albaran.entrega) {
    return <p className="text-xs text-ink-muted">Ya entregado: no se cancela. Para cambiar el resultado usa «Corregir entrega» (en Entregas); si hubo daño, registra un «Caso de daño».</p>;
  }

  const textos: Record<Paso, { titulo: string; desc: string; etiqueta: string; verbo: string; ok: string }> = {
    solicitar: { titulo: `Solicitar cancelación de ${albaran.folio}`, desc: 'Otra persona con permiso la autorizará o la rechazará. Mientras tanto el albarán sigue vigente.', etiqueta: 'Motivo de la cancelación', verbo: 'Solicitar', ok: 'Cancelación solicitada' },
    autorizar: { titulo: `Autorizar cancelación de ${albaran.folio}`, desc: 'Las piezas del albarán regresan a producto terminado. Debe autorizarla una persona distinta a la que la pidió.', etiqueta: 'Motivo de la autorización', verbo: 'Autorizar', ok: 'Cancelación autorizada' },
    rechazar: { titulo: `Rechazar cancelación de ${albaran.folio}`, desc: 'El albarán sigue vigente y queda el motivo del rechazo.', etiqueta: 'Motivo del rechazo', verbo: 'Rechazar', ok: 'Cancelación rechazada' },
  };

  return (
    <div className="flex flex-col gap-1 text-xs">
      {c?.pendiente ? (
        <>
          <Badge variant="warning">Cancelación pendiente</Badge>
          <p>
            Pidió {nombreUsuario(c.solicitadaPorId)}{c.solicitadaAt && <> · <DateTimeDisplay value={c.solicitadaAt} variant="datetime" /></>}: {c.motivoSolicitud}
          </p>
          {puedeResolver ? (
            <div className="flex flex-wrap gap-2">
              <Button size="sm" onClick={() => { setError(null); setPaso('autorizar'); }}>Autorizar</Button>
              <Button size="sm" variant="outline" onClick={() => { setError(null); setPaso('rechazar'); }}>Rechazar</Button>
            </div>
          ) : (
            <p className="text-ink-muted">Espera a que la autorice otra persona con permiso.</p>
          )}
        </>
      ) : (
        <>
          {c?.rechazadaAt && (
            <p className="text-ink-muted">
              Cancelación rechazada por {nombreUsuario(c.rechazadaPorId)} · <DateTimeDisplay value={c.rechazadaAt} variant="datetime" />: {c.motivoRechazo}
              {c.solicitadaAt && <> (la pidió {nombreUsuario(c.solicitadaPorId)}: {c.motivoSolicitud})</>}
            </p>
          )}
          {albaran.cancelado && (
            <p className="text-ink-muted">
              Cancelado{c?.autorizadaPorId ? <> · autorizó {nombreUsuario(c.autorizadaPorId)}</> : null}
              {c?.solicitadaAt && <> · pidió {nombreUsuario(c.solicitadaPorId)}: {c.motivoSolicitud}</>}
            </p>
          )}
          {operar && !albaran.cancelado && <div><Button size="sm" variant="outline" onClick={() => { setError(null); setPaso('solicitar'); }}>Solicitar cancelación</Button></div>}
        </>
      )}
      {paso && (
        <MotivoDialog
          titulo={textos[paso].titulo}
          descripcion={textos[paso].desc}
          etiqueta={textos[paso].etiqueta}
          min={5}
          max={500}
          verbo={textos[paso].verbo}
          variante={paso === 'autorizar' ? 'default' : 'secondary-danger'}
          error={error}
          onCerrar={() => setPaso(null)}
          onConfirmar={async (motivo) => {
            setError(null);
            try {
              await mutar.mutateAsync(
                paso === 'solicitar'
                  ? { hojaId, albaranId: albaran.id, paso: 'solicitar-cancelacion', body: { motivo } }
                  : { hojaId, albaranId: albaran.id, paso: 'resolver-cancelacion', body: { autorizar: paso === 'autorizar', motivo } },
              );
              toast.success(textos[paso].ok);
              setPaso(null);
            } catch (e) {
              setError(mensajeApi(e));
            }
          }}
        />
      )}
    </div>
  );
}
