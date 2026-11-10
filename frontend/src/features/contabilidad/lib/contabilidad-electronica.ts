import type { ArchivoCe, ParametrosArchivoCe, RevisionContabilidadElectronica } from '../api/contabilidad-electronica';

const ORDEN = /^[A-Z]{3}[0-9]{7}\/[0-9]{2}$/;
const TRAMITE = /^[A-Z]{2}[0-9]{12}$/;

/**
 * K10.8 · motivo por el que un archivo no se puede descargar todavía (undefined = se puede). Repite en la pantalla las reglas que
 * el servidor valida, para que el botón explique por qué está deshabilitado.
 */
export function motivoArchivoCe(
  tipo: ArchivoCe,
  r: RevisionContabilidadElectronica,
  p: ParametrosArchivoCe,
): string | undefined {
  if (!r.rfcValido) return 'La empresa necesita un RFC válido para generar los archivos del SAT.';
  if (tipo !== 'balanza' && r.mes === 13) return 'El periodo 13 solo genera la balanza de cierre.';
  if (tipo === 'catalogo' && !r.puedeGenerarCatalogo)
    return 'Corrige primero las cuentas sin código agrupador o sin naturaleza de la lista.';
  if (tipo === 'balanza' && p.tipoEnvio === 'C' && !p.fechaModificacion)
    return 'La balanza complementaria lleva la fecha de la última modificación.';
  if (tipo === 'polizas') {
    if (r.polizas === 0) return 'El periodo no tiene pólizas autorizadas.';
    const conOrden = p.solicitud === 'AF' || p.solicitud === 'FC';
    if (conOrden && !ORDEN.test(p.numOrden ?? '')) return 'Captura el número de orden con la forma ABC1234567/12.';
    if (!conOrden && !TRAMITE.test(p.numTramite ?? '')) return 'Captura el número de trámite con la forma AB123456789012.';
  }
  return undefined;
}

/**
 * K10.8 · O2: el mismo conteo que el bloqueo del servidor. «237 cuentas con 239 pendientes» cuando alguna cuenta tiene dos
 * pendientes; «237 cuentas» cuando cada una tiene uno.
 */
export function resumenProblemasCe(r: Pick<RevisionContabilidadElectronica, 'problemas' | 'cuentasConProblemas'>): string {
  const cuentas = r.cuentasConProblemas || new Set(r.problemas.map((p) => p.cuentaId)).size;
  const textoCuentas = `${cuentas} ${cuentas === 1 ? 'cuenta' : 'cuentas'}`;
  const pendientes = r.problemas.length;
  return cuentas === pendientes ? textoCuentas : `${textoCuentas} con ${pendientes} pendientes`;
}
