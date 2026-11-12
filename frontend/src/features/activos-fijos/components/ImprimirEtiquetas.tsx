import { abrirPdfParaImprimir } from '@/components/erp/reportes/imprimir-pdf';
import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { apiRequest } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { AF_BASE, afKeys } from '../api/hooks';
import type { Etiqueta } from '../api/conservacion';
function ImprimirEtiquetasAccion({
  activoId,
  sucursalId,
  claseId,
}: {
  activoId?: string;
  sucursalId?: string;
  claseId?: string;
}) {
  const qc = useQueryClient();
  const [motivo, setMotivo] = useState('');
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState('');
  const [mensaje, setMensaje] = useState('');
  return (
    <div className="space-y-2">
      <label className="text-xs">
        Motivo de impresión
        <select
          value={motivo}
          onChange={(e) => setMotivo(e.target.value)}
          className="block h-9 rounded-md border border-line-control bg-surface-card px-3"
        >
          <option value="">Primera impresión</option>
          <option>Reposición por daño</option>
          <option>Pérdida</option>
          <option>Cambio de datos</option>
        </select>
      </label>
      <Button
        variant="outline"
        disabled={ocupado}
        title="Generar PDF en hoja carta con los filtros de sucursal y clase"
        onClick={async () => {
          setOcupado(true);
          setError('');
          setMensaje('');
          try {
            let cantidad = 0;
            await abrirPdfParaImprimir(async () => {
              const parametros = new URLSearchParams();
              if (activoId) parametros.set('activoId', activoId);
              if (sucursalId) parametros.set('sucursalId', sucursalId);
              if (claseId) parametros.set('claseId', claseId);
              const { data } = await apiRequest<Etiqueta[]>(`${AF_BASE}/etiquetas?${parametros}`);
              const { generarEtiquetas } = await import('../etiquetas/generar');
              const blob = await generarEtiquetas(data);
              await apiRequest(`${AF_BASE}/etiquetas/registrar-impresion`, {
                method: 'POST',
                body: {
                  motivo: motivo || null,
                  activos: data.map((e) => ({ id: e.id, version: e.version })),
                },
                idempotencyKey: crypto.randomUUID(),
              });
              cantidad = data.length;
              return blob;
            });
            // La entrega del PDF sí es observable; la salida física de la impresora no lo es.
            await qc.invalidateQueries({ queryKey: afKeys.all });
            setMensaje(`PDF generado: ${cantidad} etiquetas. Impresión registrada.`);
          } catch (e) {
            setError((e as Error).message);
          } finally {
            setOcupado(false);
          }
        }}
      >
        {ocupado
          ? 'Generando PDF…'
          : activoId
            ? 'Imprimir etiqueta'
            : 'Imprimir etiquetas por lote'}
      </Button>
      {error && (
        <p role="alert" className="text-danger-fg">
          {error}
        </p>
      )}
      {mensaje && (
        <p role="status" className="text-xs">
          {mensaje}
        </p>
      )}
    </div>
  );
}

export function ImprimirEtiquetas(props: {
  activoId?: string;
  sucursalId?: string;
  claseId?: string;
}) {
  const permitido = useHasPermission(P.ActivosFijosEtiquetasImprimir);
  return permitido ? <ImprimirEtiquetasAccion {...props} /> : null;
}
