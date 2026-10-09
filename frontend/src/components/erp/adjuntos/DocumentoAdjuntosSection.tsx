import { useState } from 'react';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { AdjuntosManager } from './AdjuntosManager';
import {
  useAdjuntos,
  useTiposDocumento,
  useSubirAdjunto,
  useDarDeBajaAdjunto,
  useEnlaceAdjunto,
} from './api/hooks';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { esApiError } from '@/lib/api';

export function DocumentoAdjuntosSection({
  base,
  tipo,
  permiso,
}: {
  base: string;
  tipo: 'requisicion' | 'factura_proveedor';
  permiso: string;
}) {
  const canVer = useHasPermission(`${permiso}.adjuntos-ver`);
  const canSubir = useHasPermission(`${permiso}.adjuntos-subir`);
  const canBaja = useHasPermission(`${permiso}.adjuntos-baja`);
  const [verBajas, setVerBajas] = useState(false);
  const lista = useAdjuntos(base, { enabled: canVer, incluirBajas: canBaja && verBajas });
  const tipos = useTiposDocumento(tipo, { enabled: canVer });
  const subir = useSubirAdjunto(base);
  const baja = useDarDeBajaAdjunto(base);
  const enlace = useEnlaceAdjunto(base);
  if (!canVer) return null;
  const error = lista.error ?? tipos.error;
  if (lista.isError || tipos.isError)
    return (
      <ErrorState
        problem={esApiError(error) ? error.problem : undefined}
        onRetry={() => {
          void lista.refetch();
          void tipos.refetch();
        }}
      />
    );
  if (lista.isLoading || tipos.isLoading)
    return <TableSkeleton rows={3} columns={[{ width: 'w-full' }]} />;
  return (
    <section
      className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
      aria-label="Adjuntos"
    >
      <h2 className="text-md font-semibold text-ink">Adjuntos</h2>
      <AdjuntosManager
        adjuntos={(lista.data ?? []).map((x) => ({
          ...x,
          fechaCarga: x.subidoEn,
          usuarioCargaId: x.subidoPorId,
        }))}
        tipos={(tipos.data ?? []).map((x) => ({ ...x, clave: x.codigo, descripcion: x.nombre }))}
        canUpload={canSubir}
        canRemove={canBaja}
        modoBaja
        puedeVerBajas={canBaja}
        verBajas={verBajas}
        onVerBajasChange={setVerBajas}
        onUpload={async (args) => {
          await subir.subir(args);
        }}
        onDarDeBaja={async (adjuntoId, motivo) => {
          await baja.mutateAsync({ adjuntoId, motivo });
        }}
        onDescargar={async (adjuntoId) => {
          await enlace.mutateAsync({ adjuntoId });
        }}
        uploadProgress={subir.progress}
        isUploading={subir.isUploading}
      />
    </section>
  );
}
