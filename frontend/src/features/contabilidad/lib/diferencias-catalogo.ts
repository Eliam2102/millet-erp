import type { CambioCuenta, Cuenta } from '../api/types';

const CAMPOS: readonly [keyof Cuenta, string][] = [
  ['nombre', 'Nombre'], ['padreId', 'Cuenta padre'], ['nivel', 'Nivel'], ['naturaleza', 'Naturaleza'],
  ['tipo', 'Tipo'], ['estatus', 'Estado'], ['cuentaControl', 'Cuenta colectiva'],
  ['noAfectableManual', 'No afectable por asiento manual'], ['codigoAgrupador', 'Código agrupador'],
  ['grupoReporte', 'Grupo de reporte'], ['clase', 'Clase'], ['rubroId', 'Rubro'],
];

/** Campos que guardan el id de otra cuenta: se muestran con su código. */
const REFERENCIAS: ReadonlySet<keyof Cuenta> = new Set(['padreId', 'rubroId']);

/** Compara por id; `codigos` (id → código, del API) solo cambia lo que se muestra. */
export function diferenciasCatalogo(cambio: CambioCuenta, codigos: Record<string, string> = {}) {
  const ver = (campo: keyof Cuenta, valor: unknown) =>
    REFERENCIAS.has(campo) && typeof valor === 'string' ? codigos[valor] ?? valor : valor;
  return CAMPOS.filter(([campo]) => !cambio.antes || cambio.antes[campo] !== cambio.despues[campo])
    .map(([campo, etiqueta]) => ({
      campo, etiqueta,
      antes: cambio.antes ? ver(campo, cambio.antes[campo]) : undefined,
      despues: ver(campo, cambio.despues[campo]),
    }));
}

export function valorDiferencia(valor: unknown): string {
  if (valor === undefined || valor === null || valor === '') return 'Sin valor';
  if (typeof valor === 'boolean') return valor ? 'Sí' : 'No';
  if (valor === 'Titulo') return 'Acumula';
  return String(valor);
}
