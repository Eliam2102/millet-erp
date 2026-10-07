import type { AdjuntoItem } from '@/components/erp/adjuntos/AdjuntosManager';
import type { TipoDocumentoSelectorItem } from '@/components/erp/adjuntos/TipoDocumentoSelector';
import type { AdjuntoResponse, TipoDocumentoAdjunto } from './types';

/** AdjuntoResponse (servicio genérico) -> shape que renderiza `AdjuntosManager`. */
export function aAdjuntoItem(a: AdjuntoResponse): AdjuntoItem {
  return {
    id: a.id,
    tipoDocumentoId: a.tipoDocumentoId,
    nombreArchivo: a.nombreArchivo,
    contentType: a.contentType,
    tamanoBytes: a.tamanoBytes,
    fechaCarga: a.subidoEn,
    usuarioCargaId: a.subidoPorId,
    estado: a.estado,
    vigenteHasta: a.vigenteHasta,
    hashSha256: a.hashSha256,
    bajaEn: a.bajaEn,
    bajaMotivo: a.bajaMotivo,
  };
}

/** Tipo del catálogo -> item del selector. */
export function aTipoSelector(t: TipoDocumentoAdjunto): TipoDocumentoSelectorItem {
  return {
    id: t.id,
    clave: t.codigo,
    descripcion: t.nombre,
    vigenciaMeses: t.vigenciaMeses,
  };
}
