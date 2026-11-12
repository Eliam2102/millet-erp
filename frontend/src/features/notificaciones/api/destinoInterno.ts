/** Defensa de presentación; el servidor valida catálogo, contexto y facultad. */
export function destinoInterno(destino: string) {
  return destino.startsWith('/') && !destino.startsWith('//') && !destino.includes('\\') &&
    !destino.includes('://') && !Array.from(destino).some(c => c.charCodeAt(0) <= 32);
}
