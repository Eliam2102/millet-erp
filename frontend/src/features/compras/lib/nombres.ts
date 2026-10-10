/**
 * Helpers de presentación para los nombres resueltos en backend (ADR-0042).
 * El backend entrega `requisitanteNombre` y `departamentoNombre`/`Clave` en
 * los DTOs de lista y detalle; aquí se formatean con una etiqueta por confirmar para que
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

/** Etiqueta del departamento: `CLAVE · Nombre` si hay datos; si falta, indica Por confirmar. */
export function departamentoLabel(r: ConDepartamento): string {
  if (r.departamentoClave && r.departamentoNombre) {
    return `${r.departamentoClave} · ${r.departamentoNombre}`;
  }
  return r.departamentoNombre?.trim() || r.departamentoClave?.trim() || '[DEPARTAMENTO POR CONFIRMAR]';
}

/** Nombre del requisitante resuelto en backend, o Por confirmar si no se resolvió. */
export function requisitanteLabel(r: ConRequisitante): string {
  return r.requisitanteNombre?.trim() || '[REQUISITANTE POR CONFIRMAR]';
}
