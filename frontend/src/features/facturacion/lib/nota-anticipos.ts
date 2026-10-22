/**
 * Texto prellenado de las «Observaciones» de una factura con los anticipos
 * aplicados (86e3tfdw2). Espejo de `NotaAnticiposAplicados.Componer` del
 * backend: mismo formato es-MX explícito (coma de miles, punto decimal,
 * redondeo a 2 decimales «half away from zero»).
 *
 * «Anticipos aplicados: FANT-12 $5,000.00 MXN; FANT-15 $1,200.00 MXN».
 * Cadena vacía si no hay anticipos.
 */
export interface AnticipoNota {
  folio: string;
  importe: number;
  moneda: string;
}

const ENCABEZADO = 'Anticipos aplicados: ';

// Grupos de 3 con coma y punto decimal — igual que es-MX, sin depender de
// los datos de locale del navegador (algunos motores no agrupan 4 dígitos).
const FORMATO = new Intl.NumberFormat('en-US', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
  useGrouping: true,
});

/** Redondeo a centavos «half away from zero» tolerante al error de coma flotante. */
function redondear2(valor: number): number {
  const centavos = Number((Math.abs(valor) * 100).toPrecision(15));
  return (Math.sign(valor) * Math.round(centavos)) / 100;
}

export function notaAnticiposAplicados(anticipos: readonly AnticipoNota[]): string {
  const partes = anticipos.map(
    (a) =>
      `${a.folio.trim()} $${FORMATO.format(redondear2(a.importe))} ${a.moneda.trim().toUpperCase()}`,
  );
  return partes.length === 0 ? '' : ENCABEZADO + partes.join('; ');
}
