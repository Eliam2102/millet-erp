import { useMemo } from 'react';
import { toast } from 'sonner';
import {
  AdjuntosManager,
  type AdjuntoItem,
} from '@/components/erp';
import { useTiposDocumentoOc } from '@/features/catalogos/api';
import {
  accionAdjuntarDocumento,
  accionRemoverAdjunto,
} from '@/features/compras/ordenes/lib/acciones-disponibles';
import type { OrdenCompraDetalleResponse } from '@/features/compras/ordenes/api/types';
import { useAdjuntarDocumento } from '@/features/compras/ordenes/api/useAdjuntarDocumento';
import { useRemoverAdjunto } from '@/features/compras/ordenes/api/useRemoverAdjunto';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { useQueryClient } from '@tanstack/react-query';
import { handleOcMutationError } from '@/features/compras/ordenes/lib/handle-conflict';
import { useUsuarios, mapById } from '@/features/catalogos/api';

/**
 * <c>&lt;AdjuntosManagerOc/&gt;</c> — wrapper OC-specific del
 * <c>&lt;AdjuntosManager/&gt;</c> cross-módulo (UF3-PR2). Carga el
 * catálogo <c>tipos-documento-oc</c> y wira los hooks de mutación
 * de OC contra los callbacks parametrizados del manager genérico.
 *
 * <para>Marca <c>ficha_tecnica</c> como obligatorio si la OC es de
 * importación (FOC2 §4.12). El backend lo valida al transmitir; este
 * componente solo decora la lista para que el comprador anticipe el
 * requisito.</para>
 */
export interface AdjuntosManagerOcProps {
  oc: OrdenCompraDetalleResponse;
}

export function AdjuntosManagerOc({ oc }: AdjuntosManagerOcProps) {
  const permisos = useAuthStore((s) => s.permisos);
  const accAdjuntar = accionAdjuntarDocumento(oc, permisos);
  const accRemover = accionRemoverAdjunto(oc, permisos);

  const tiposQuery = useTiposDocumentoOc();
  const adjuntarMut = useAdjuntarDocumento();
  const removerMut = useRemoverAdjunto();
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();

  const usuariosQuery = useUsuarios();
  const usuariosMap = useMemo(
    () => mapById(usuariosQuery.data?.items),
    [usuariosQuery.data],
  );

  const obligatorios = useMemo(() => {
    if (!oc.esImportacion || tiposQuery.data == null) return [];
    return tiposQuery.data
      .filter((t) => t.obligatorioSiImportacion)
      .map((t) => t.id);
  }, [oc.esImportacion, tiposQuery.data]);

  // Adjuntos del agregado (UF3-PR2 los expone en el detalle).
  const adjuntos = oc.adjuntos as readonly AdjuntoItem[];

  async function handleUpload({
    archivo,
    tipoDocumentoId,
  }: {
    archivo: File;
    tipoDocumentoId: string;
  }) {
    try {
      await adjuntarMut.adjuntar({
        ordenCompraId: oc.id,
        tipoDocumentoId,
        archivo,
        idempotencyKey: crypto.randomUUID(),
      });
      toast.success(`"${archivo.name}" adjuntado.`);
    } catch (err) {
      // 409 → ConflictDialog en modo simple. NO re-throw para evitar
      // doble notificación (el dialog ya guía al usuario).
      if (
        handleOcMutationError(err, {
          ordenCompraId: oc.id,
          conflictDialog,
          queryClient,
        })
      ) {
        return;
      }
      const msg =
        err instanceof Error ? err.message : 'Error desconocido al subir.';
      toast.error('No se pudo subir el archivo.', { description: msg });
      throw err;
    }
  }

  async function handleRemove(adjuntoId: string) {
    try {
      await removerMut.mutateAsync({ ordenCompraId: oc.id, adjuntoId });
      toast.success('Adjunto eliminado.');
    } catch (err) {
      if (
        handleOcMutationError(err, {
          ordenCompraId: oc.id,
          conflictDialog,
          queryClient,
        })
      ) {
        return;
      }
      const msg =
        err instanceof Error ? err.message : 'Error desconocido.';
      toast.error('No se pudo eliminar el adjunto.', { description: msg });
    }
  }

  return (
    <AdjuntosManager
      adjuntos={adjuntos}
      tipos={tiposQuery.data ?? []}
      tiposLoading={tiposQuery.isLoading}
      onUpload={handleUpload}
      onRemove={accRemover.visible && accRemover.habilitada ? handleRemove : undefined}
      uploadProgress={adjuntarMut.progress}
      isUploading={adjuntarMut.isUploading}
      canUpload={accAdjuntar.visible && accAdjuntar.habilitada}
      canRemove={accRemover.visible && accRemover.habilitada}
      obligatorios={obligatorios}
      resolverNombreUsuario={(id) => usuariosMap.get(id)?.nombre ?? null}
      resolverContenidoUrl={(a) =>
        `/api/v1/compras/ordenes/${oc.id}/adjuntos/${a.id}/contenido`
      }
    />
  );
}
