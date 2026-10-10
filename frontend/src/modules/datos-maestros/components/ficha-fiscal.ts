import type { ProductoAwDetalle } from '../api/types';
export function estadoFiscalTexto(estado: number | string | undefined) {
  return estado === 2 || estado === 'Confirmada' ? 'Confirmada' : estado === 1 || estado === 'PorReglaSinRevisar' ? 'Por regla, sin revisar' : 'Pendiente de captura fiscal';
}
export function faltantesFicha(p: Pick<ProductoAwDetalle, 'claveProdServSat' | 'claveUnidadSat' | 'objetoImp' | 'tasaIvaTraslado'>) {
  return [!p.claveProdServSat ? 'clave de producto SAT' : null, !p.claveUnidadSat ? 'clave de unidad SAT' : null,
    !p.objetoImp ? 'objeto de impuesto' : null, p.tasaIvaTraslado == null ? 'tasa de IVA' : null].filter((v): v is string => v !== null);
}

/** Traduce el historial almacenado a cambios fiscales legibles, sin exponer JSON. */
export function cambiosFiscalesTexto(antesJson: string, despuesJson: string): string[] {
  function leer(texto: string): Record<string, unknown> {
    try {
      const dato: unknown = JSON.parse(texto);
      if (typeof dato !== 'object' || dato === null || Array.isArray(dato)) return {};
      const objeto = dato as Record<string, unknown>;
      const ficha = objeto.Ficha;
      return typeof ficha === 'object' && ficha !== null && !Array.isArray(ficha)
        ? { ...objeto, ...ficha as Record<string, unknown> } : objeto;
    } catch { return {}; }
  }
  const antes = leer(antesJson); const despues = leer(despuesJson);
  const etiquetas: Record<string, string> = {
    ClaveProdServSat: 'Clave de producto SAT', ClaveUnidadSat: 'Clave de unidad SAT', ObjetoImp: 'Objeto de impuesto',
    TasaIvaTraslado: 'IVA', TasaRetencionIva: 'Retención de IVA', TasaRetencionIsr: 'Retención de ISR',
    Campo: 'Campo de familia', Valor: 'Familia', Descripcion: 'Descripción', Activa: 'Regla activa',
    EstadoFiscal: 'Estado fiscal', OrigenFiscal: 'Origen fiscal', ConfirmadoPor: 'Confirmó', ConfirmadoEn: 'Fecha de confirmación',
  };
  function mostrar(clave: string, valor: unknown): string {
    if (valor == null || valor === '') return 'Sin dato';
    if (clave === 'EstadoFiscal' && (typeof valor === 'number' || typeof valor === 'string')) return estadoFiscalTexto(valor);
    if (clave === 'OrigenFiscal') return valor === 1 ? 'Regla de familia' : valor === 2 ? 'Excepción del producto' : 'Pendiente';
    if (clave.startsWith('Tasa') && typeof valor === 'number') return `${Number((valor * 100).toFixed(6))} %`;
    if (typeof valor === 'boolean') return valor ? 'Sí' : 'No';
    return typeof valor === 'string' || typeof valor === 'number' ? String(valor) : 'Sin dato';
  }
  const cambios = Object.entries(etiquetas).filter(([clave]) => antes[clave] !== despues[clave])
    .map(([clave, etiqueta]) => `${etiqueta}: ${mostrar(clave, antes[clave])} → ${mostrar(clave, despues[clave])}`);
  return cambios.length > 0 ? cambios : ['Sin cambios en los datos fiscales.'];
}
