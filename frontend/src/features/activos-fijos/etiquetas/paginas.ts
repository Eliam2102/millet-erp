import type { Etiqueta } from '../api/conservacion';
import { validarNumero } from './codigos';
export function paginasEtiquetas(etiquetas: Etiqueta[]) {
  etiquetas.forEach((e) => validarNumero(e.numero));
  const columnas = etiquetas.some((e) => e.numero.length > 26) ? 1 : 2;
  const porPagina = columnas * 5;
  return {
    columnas,
    paginas: Array.from({ length: Math.ceil(etiquetas.length / porPagina) }, (_, i) =>
      etiquetas.slice(i * porPagina, (i + 1) * porPagina),
    ),
  };
}
