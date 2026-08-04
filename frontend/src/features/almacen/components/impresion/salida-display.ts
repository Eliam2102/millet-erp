import type { SalidaDetalle, SalidaLineaItem } from '@/features/almacen/api/types';
import { formatCcMaquinaLabel } from '@/features/centros-costo/lib/cc-maquina-label';

/**
 * Helpers de presentación del detalle de salida (ADR-0042). Centralizan la
 * regla "nombre resuelto en backend, con fallback al id/clave cruda" para que
 * la pantalla de detalle y el comprobante PDF la apliquen idéntica y sea
 * testeable sin renderizar react-pdf.
 */

/** Sub-almacén: "CLAVE · Nombre" si hay nombre; si no, el id crudo. */
export function subAlmacenLabel(salida: SalidaDetalle): string {
  if (!salida.subAlmacenNombre) return salida.subAlmacenId;
  return salida.subAlmacenClave
    ? `${salida.subAlmacenClave} · ${salida.subAlmacenNombre}`
    : salida.subAlmacenNombre;
}

/** Persona destinataria: nombre si hay; si no, el id crudo; si no hay, '—'. */
export function destinatarioLabel(salida: SalidaDetalle): string {
  return (
    salida.personaDestinatariaNombre ?? salida.personaDestinatariaId ?? '—'
  );
}

/** Folio de la RQ vinculada; si no resuelve, el id; null si no hay RQ. */
export function requisicionLabel(salida: SalidaDetalle): string | null {
  return salida.rqFolio ?? salida.rqId;
}

/** Folio de la RQ regularizadora; si no resuelve, el id; null si no hay. */
export function rqRegularizadoraLabel(salida: SalidaDetalle): string | null {
  return salida.rqRegularizadoraFolio ?? salida.rqRegularizadoraId;
}

/** Artículo de una línea: "CLAVE · Descripción" si hay; si no, el id crudo. */
export function articuloLabel(linea: SalidaLineaItem): string {
  if (!linea.articuloDescripcion) return linea.articuloId;
  return linea.articuloClave
    ? `${linea.articuloClave} · ${linea.articuloDescripcion}`
    : linea.articuloDescripcion;
}

/**
 * CC-Máquina de una línea para el detalle/PDF (Fase E PR5): "clave — nombre"
 * (formatCcMaquinaLabel), "No catalogado" si el id es irresoluble, y guion largo
 * "—" (U+2014) si la línea no lleva CC.
 */
export function ccMaquinaLabel(linea: SalidaLineaItem): string {
  return linea.centroCostoId
    ? formatCcMaquinaLabel({
        clave: linea.centroCostoClave,
        nombre: linea.centroCostoNombre,
      })
    : '—';
}
