/**
 * ÚNICO punto de traducción Dim ↔ etiqueta del frontend (07 §0: "helper
 * único, no strings sueltos"; tabla canónica en 05 §0). El API habla Dim
 * (`dim1`/`dim2`/`dim3`/grupos) y NO hornea etiquetas; la UI es
 * contextual:
 *
 * - Configuración y asignación: "Dimensión 1/2/3" y "Grupo dimensión 2/3"
 *   (NUNCA Sucursal/Departamento/Máquina).
 * - Documentos (Fase E): "Máquina" para Dim3; Dim1/Dim2 solo se muestran
 *   heredadas.
 *
 * Este archivo es el ÚNICO lugar del src donde pueden vivir esos
 * literales: la regla `no-restricted-syntax` de `eslint.config.js` los
 * prohíbe fuera de aquí (CI corre lint — enforcement real, no disciplina).
 */

/** Niveles y grupos del modelo Dim (claves de código, no de UI). */
export type NivelDim = 'dim1' | 'dim2' | 'dim3' | 'grupoDim2' | 'grupoDim3';

/**
 * Contexto de UI. `configuracion` y `asignacion` comparten vocabulario
 * (05 §0); `documentos` llega en la Fase E (FE-PR4/5) y ya queda cubierto
 * para que MaquinaSelector no invente strings.
 */
export type ContextoUi = 'configuracion' | 'asignacion' | 'documentos';

const ETIQUETAS_CATALOGO: Record<NivelDim, string> = {
  dim1: 'Dimensión 1',
  dim2: 'Dimensión 2',
  dim3: 'Dimensión 3',
  grupoDim2: 'Grupo dimensión 2',
  grupoDim3: 'Grupo dimensión 3',
};

const ETIQUETAS_DOCUMENTOS: Record<NivelDim, string> = {
  // En documentos SOLO Dim3 se captura; las superiores se muestran
  // heredadas con su nombre propio (ej. "Corte · Conkal"), no con la
  // etiqueta del nivel — estas dos no deberían pintarse como label de
  // campo, pero si alguien las pide, la etiqueta neutra es la de catálogo.
  dim1: 'Dimensión 1',
  dim2: 'Dimensión 2',
  dim3: 'Máquina',
  grupoDim2: 'Grupo dimensión 2',
  grupoDim3: 'Grupo dimensión 3',
};

/** Etiqueta visible de un nivel del modelo Dim según el contexto de UI. */
export function etiquetaNivel(nivel: NivelDim, contexto: ContextoUi): string {
  return contexto === 'documentos'
    ? ETIQUETAS_DOCUMENTOS[nivel]
    : ETIQUETAS_CATALOGO[nivel];
}

/**
 * Etiqueta del tipo de nodo que devuelve la jerarquía
 * (`NodoCeCo.tipo`), mismo contrato que `etiquetaNivel`.
 */
export function etiquetaTipoNodo(
  tipo: 'dim1' | 'dim2' | 'dim3',
  contexto: ContextoUi,
): string {
  return etiquetaNivel(tipo, contexto);
}
