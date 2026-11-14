/**
 * Dominios corporativos del correo de colaborador. Se leen de
 * `VITE_DOMINIOS_CORPORATIVOS` (lista separada por comas) para no fijar en el
 * código el tenant de un ambiente. El backend valida aparte contra
 * `Entra:DominiosPermitidos`.
 */
export const DOMINIOS_CORPORATIVOS: string[] = (import.meta.env.VITE_DOMINIOS_CORPORATIVOS ?? '')
  .split(',')
  .map((d: string) => d.trim().toLowerCase())
  .filter(Boolean);

/** Dominio que se propone por omisión; vacío si el ambiente no configuró ninguno. */
export const DOMINIO_POR_OMISION = DOMINIOS_CORPORATIVOS[0] ?? '';
