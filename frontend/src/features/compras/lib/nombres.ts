/**
 * Helpers de presentación para los nombres resueltos en backend (ADR-0042).
 * El backend entrega `requisitanteNombre` y `departamentoNombre`/`Clave` en
 * los DTOs de lista y detalle; aquí se formatean con una etiqueta de dato faltante para que
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

/** Etiqueta del departamento: `CLAVE · Nombre` si hay datos; si falta, indica el dato faltante. */
export function departamentoLabel(r: ConDepartamento): string {
  if (r.departamentoClave && r.departamentoNombre) {
    return `${r.departamentoClave} · ${r.departamentoNombre}`;
  }
  return r.departamentoNombre?.trim() || r.departamentoClave?.trim() || 'Sin departamento';
}

/** Nombre del requisitante resuelto en backend, o Sin asignar si no se resolvió. */
export function requisitanteLabel(r: ConRequisitante): string {
  return r.requisitanteNombre?.trim() || 'Sin asignar';
}
