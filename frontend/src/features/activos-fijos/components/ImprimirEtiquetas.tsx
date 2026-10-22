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
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState('');
  const [mensaje, setMensaje] = useState('');
  return (
    <div className="space-y-2">
      <Button
        variant="outline"
        disabled={ocupado}
        title="Generar PDF en hoja carta con los filtros de sucursal y clase"
        onClick={async () => {
          setOcupado(true);
          setError('');
          setMensaje('');
          try {
            const parametros = new URLSearchParams();
            if (activoId) parametros.set('activoId', activoId);
            if (sucursalId) parametros.set('sucursalId', sucursalId);
            if (claseId) parametros.set('claseId', claseId);
            const { data } = await apiRequest<Etiqueta[]>(`${AF_BASE}/etiquetas?${parametros}`);
            const { generarEtiquetas } = await import('../etiquetas/generar');
            const blob = await generarEtiquetas(data);
            const url = URL.createObjectURL(blob);
            const enlace = document.createElement('a');
            enlace.href = url;
            enlace.download = 'etiquetas-activos.pdf';
            enlace.click();
            setTimeout(() => URL.revokeObjectURL(url), 60000);
            // La entrega del PDF sí es observable; la salida física de la impresora no lo es.
            await apiRequest(`${AF_BASE}/etiquetas/registrar-impresion`, {
              method: 'POST',
              body: { activos: data.map((e) => ({ id: e.id, version: e.version })) },
              idempotencyKey: crypto.randomUUID(),
            });
            await qc.invalidateQueries({ queryKey: afKeys.all });
            setMensaje(`PDF generado: ${data.length} etiquetas. Impresión registrada.`);
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
