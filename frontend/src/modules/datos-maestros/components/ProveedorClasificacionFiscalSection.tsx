import { erroresDeClasificacion } from '@/modules/datos-maestros/lib/clasificacion-fiscal';
import { useState } from 'react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { etiquetaConcepto } from '@/features/cxp/lib/retenciones-p8';
import { useActualizarClasificacionFiscalProveedor } from '../api/proveedores';
import { OrigenProveedor } from '../api/types';
import {
  ClasificacionFiscalCampos,
  type ClasificacionFiscalValores,
  type ErroresClasificacionFiscal,
} from './ClasificacionFiscalCampos';

interface Props {
  proveedorId: string;
  origen: OrigenProveedor | null;
  regimenFiscalCodigo: string | null;
  conceptoRetencion: string | null;
}

export function ProveedorClasificacionFiscalSection({ proveedorId, origen, regimenFiscalCodigo, conceptoRetencion }: Props) {
  const puedeEditar = useHasPermission(PermisosCanonicos.DatosMaestrosProveedoresGestionar);
  const [editando, setEditando] = useState(false);
  const [valores, setValores] = useState<ClasificacionFiscalValores>({ origen, regimenFiscalCodigo, conceptoRetencion });
  const [errores, setErrores] = useState<ErroresClasificacionFiscal>({});
  const actualizar = useActualizarClasificacionFiscalProveedor();
  const tituloId = `clasificacion-${proveedorId}-titulo`;
  const porRevisar = origen == null || conceptoRetencion == null;

  function guardar(event: React.FormEvent) {
    event.preventDefault();
    if (actualizar.isPending) return;
    setErrores({});
    actualizar.mutate({ id: proveedorId, payload: valores, idempotencyKey: crypto.randomUUID() }, {
      onSuccess: () => {
        setEditando(false);
        toast.success('Clasificación fiscal actualizada');
      },
      onError: (error) => {
        if (esApiError(error)) {
          const mensaje = error.problem.detail ?? error.problem.title;
          const campo = erroresDeClasificacion(error.code, mensaje);
          if (Object.keys(campo).length > 0) { setErrores(campo); return; }
          toast.error('No se pudo guardar la clasificación fiscal', { description: mensaje });
          return;
        }
        toast.error('No se pudo guardar la clasificación fiscal', { description: 'Intenta de nuevo.' });
      },
    });
  }

  return (
    <section aria-labelledby={tituloId} className="mt-4 max-w-3xl rounded-lg bg-surface-card p-4 shadow-card-flat">
      <div className="mb-3 flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <h3 id={tituloId} className="text-sm font-semibold text-ink">Clasificación fiscal</h3>
          {porRevisar && <Badge variant="warning">Por revisar</Badge>}
        </div>
        {puedeEditar && !editando && (
          <Button variant="ghost" size="sm" onClick={() => {
            setValores({ origen, regimenFiscalCodigo, conceptoRetencion });
            setErrores({});
            setEditando(true);
          }}>Editar clasificación</Button>
        )}
      </div>
      {editando && puedeEditar ? (
        <form onSubmit={guardar} className="space-y-3">
          <ClasificacionFiscalCampos idBase={`clasif-${proveedorId}`} valores={valores} onChange={setValores}
            errores={errores} disabled={actualizar.isPending} />
          <div className="flex justify-end gap-2">
            <Button type="button" variant="ghost" size="lg" disabled={actualizar.isPending} onClick={() => setEditando(false)}>Cancelar</Button>
            <Button type="submit" size="lg" disabled={actualizar.isPending}
              title={actualizar.isPending ? 'Guardando la clasificación' : undefined}>
              {actualizar.isPending ? 'Guardando…' : 'Guardar clasificación'}
            </Button>
          </div>
        </form>
      ) : (
        <dl className="grid grid-cols-1 gap-3 text-sm sm:grid-cols-3">
          <div><dt className="text-xs text-ink-muted">Origen</dt>
            <dd className="text-ink">{origen == null ? 'Por revisar' : origen === OrigenProveedor.Extranjero ? 'Extranjero' : 'Nacional'}</dd></div>
          <div><dt className="text-xs text-ink-muted">Régimen fiscal</dt>
            <dd className="font-mono text-ink">{origen === OrigenProveedor.Extranjero ? 'No aplica' : (regimenFiscalCodigo ?? 'Sin definir')}</dd></div>
          <div><dt className="text-xs text-ink-muted">Retención que aplica</dt>
            <dd className="text-ink">{conceptoRetencion == null ? 'Por revisar' : etiquetaConcepto(conceptoRetencion)}</dd></div>
        </dl>
      )}
      {!editando && <p className="mt-3 text-xs text-ink-muted">CxP la compara contra el XML de cada factura.</p>}
    </section>
  );
}
