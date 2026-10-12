/**
 * Convierte un estado en PascalCase/camelCase a texto legible: "EnSurtido" →
 * "En surtido". Lo que ya trae espacios, no son solo letras o no tiene una
 * mayúscula interna (ej. "Autorizada") se devuelve igual.
 */
export function humanizarEstado(estado: string): string {
  const palabras = estado.split(/(?=[A-Z])/);
  if (!/^[A-Za-z]+$/.test(estado) || palabras.length < 2) {
    return estado;
  }
  const texto = palabras
    .map((palabra, i) => (i === 0 ? palabra : palabra.toLowerCase()))
    .join(' ');
  return texto.charAt(0).toUpperCase() + texto.slice(1);
}
