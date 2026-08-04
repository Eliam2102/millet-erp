/**
 * Helpers de presentación para los nombres resueltos en backend (ADR-0042).
 * El backend entrega `requisitanteNombre` y `departamentoNombre`/`Clave` en
 * los DTOs de lista y detalle; aquí se formatean con fallback al id para que
 * el front nunca dependa de leer los catálogos completos.
 */

interface ConDepartamento {
  departamentoClave: string | null;
  departamentoNombre: string | null;
  departamentoId: string;
}

interface ConRequisitante {
  requisitanteNombre: string | null;
  requisitanteId: string;
}

/** Etiqueta del departamento: `CLAVE · Nombre` si hay datos; si no, el id. */
export function departamentoLabel(r: ConDepartamento): string {
  if (r.departamentoClave && r.departamentoNombre) {
    return `${r.departamentoClave} · ${r.departamentoNombre}`;
  }
  return r.departamentoNombre ?? r.departamentoId;
}

/** Nombre del requisitante resuelto en backend, o el id como fallback. */
export function requisitanteLabel(r: ConRequisitante): string {
  return r.requisitanteNombre ?? r.requisitanteId;
}
