import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ApiError } from '@/lib/api/error';
import { apiFetch } from '@/lib/auth/api-client';

/**
 * K10.8 · Contabilidad electrónica del SAT (anexo 24, versión 1.3).
 * Espejo de `ContabilidadElectronicaQueries.cs` (GET /api/v1/contabilidad/contabilidad-electronica/...).
 */
export interface ProblemaCuentaCe {
  cuentaId: string;
  codigo: string;
  nombre: string;
  codigoAgrupador: string | null;
  problema: string;
}

export interface RevisionContabilidadElectronica {
  anio: number;
  mes: number;
  rfc: string | null;
  rfcValido: boolean;
  estadoPeriodo: string;
  cuentasCatalogo: number;
  problemas: ProblemaCuentaCe[];
  puedeGenerarCatalogo: boolean;
  polizas: number;
  polizasConCfdi: number;
  avisos: string[];
  /** Cuentas distintas con pendientes: una cuenta puede tener dos (sin código agrupador y sin naturaleza). */
  cuentasConProblemas: number;
}

export type ArchivoCe = 'catalogo' | 'balanza' | 'polizas';
export type TipoEnvioBalanza = 'N' | 'C';
export type TipoSolicitudPolizas = 'AF' | 'FC' | 'DE' | 'CO';

export interface ParametrosArchivoCe {
  tipo: ArchivoCe;
  anio: number;
  mes: number;
  tipoEnvio?: TipoEnvioBalanza;
  fechaModificacion?: string;
  solicitud?: TipoSolicitudPolizas;
  numOrden?: string;
  numTramite?: string;
  formato: 'zip' | 'xml';
  /** RFC de la empresa (de la revisión): arma el nombre del SAT si el navegador no deja leer Content-Disposition. */
  rfc?: string | null;
}

const BASE = '/api/v1/contabilidad/contabilidad-electronica';

export const contabilidadElectronicaKeys = {
  revision: (anio: number, mes: number) => ['contabilidad', 'contabilidad-electronica', anio, mes] as const,
};

export function useRevisionContabilidadElectronica(anio: number, mes: number) {
  return useQuery({
    queryKey: contabilidadElectronicaKeys.revision(anio, mes),
    queryFn: async ({ signal }) =>
      (await apiRequest<RevisionContabilidadElectronica>(`${BASE}/revision?anio=${anio}&mes=${mes}`, { signal })).data,
  });
}

/** Ruta de descarga con solo los parámetros que aplican al archivo. */
export function rutaArchivoCe(p: ParametrosArchivoCe): string {
  const q = new URLSearchParams({ anio: String(p.anio), mes: String(p.mes), formato: p.formato });
  if (p.tipo === 'balanza' && p.tipoEnvio) {
    q.set('tipoEnvio', p.tipoEnvio);
    if (p.tipoEnvio === 'C' && p.fechaModificacion) q.set('fechaModificacion', p.fechaModificacion);
  }
  if (p.tipo === 'polizas' && p.solicitud) {
    q.set('solicitud', p.solicitud);
    if ((p.solicitud === 'AF' || p.solicitud === 'FC') && p.numOrden) q.set('numOrden', p.numOrden);
    if ((p.solicitud === 'DE' || p.solicitud === 'CO') && p.numTramite) q.set('numTramite', p.numTramite);
  }
  return `${BASE}/${p.tipo}?${q.toString()}`;
}

/** Nombre del archivo que manda el servidor (RFC + año + mes + CT/BN/BC/PL), o uno de respaldo. */
export function nombreDeDisposicion(disposicion: string | null, respaldo: string): string {
  const m = disposicion?.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
  return m ? decodeURIComponent(m[1]) : respaldo;
}

/**
 * Nombre del SAT armado en la pantalla, igual al del servidor: RFC + año + mes (dos dígitos) + CT, BN, BC o PL, con la extensión
 * del formato. Sin RFC, el nombre genérico.
 */
export function nombreSatArchivoCe(p: ParametrosArchivoCe): string {
  const mes = String(p.mes).padStart(2, '0');
  const rfc = p.rfc?.trim().toUpperCase();
  if (!rfc) return `contabilidad-electronica-${p.tipo}-${p.anio}-${mes}.${p.formato}`;
  const sufijo = p.tipo === 'catalogo' ? 'CT' : p.tipo === 'polizas' ? 'PL' : p.tipoEnvio === 'C' ? 'BC' : 'BN';
  return `${rfc}${p.anio}${mes}${sufijo}.${p.formato}`;
}

/** Descarga el archivo (el endpoint pide Bearer, así que se baja como Blob). Si el servidor lo rechaza, lanza el ApiError. */
export async function descargarArchivoCe(p: ParametrosArchivoCe): Promise<string> {
  const response = await apiFetch(rutaArchivoCe(p));
  if (!response.ok) {
    let problem;
    try {
      problem = await response.json();
    } catch {
      problem = {
        type: 'about:blank',
        title: response.statusText || 'No se pudo generar el archivo',
        status: response.status,
        code: 'CE_DESCARGA_ERROR',
      };
    }
    throw new ApiError(problem, response.status);
  }
  const nombre = nombreDeDisposicion(response.headers.get('Content-Disposition'), nombreSatArchivoCe(p));
  const url = URL.createObjectURL(await response.blob());
  const enlace = document.createElement('a');
  enlace.href = url;
  enlace.download = nombre;
  document.body.appendChild(enlace);
  enlace.click();
  enlace.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
  return nombre;
}
