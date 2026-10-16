/**
 * Nombre de un usuario para mostrar en pantalla. Si el nombre no se conoce
 * (catálogo de usuarios no legible o id sin resolver), devuelve «—».
 * Nunca devuelve un id: un UUID crudo no es un dato para el usuario final.
 */
export function nombreUsuarioOGuion(nombre?: string | null): string {
  return nombre?.trim() || '—';
}
