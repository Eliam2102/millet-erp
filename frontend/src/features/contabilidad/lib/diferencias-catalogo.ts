import type { CambioCuenta, Cuenta } from '../api/types';

const CAMPOS: readonly [keyof Cuenta, string][] = [
  ['nombre', 'Nombre'], ['padreId', 'Cuenta padre'], ['nivel', 'Nivel'], ['naturaleza', 'Naturaleza'],
  ['tipo', 'Tipo'], ['estatus', 'Estado'], ['cuentaControl', 'Cuenta colectiva'],
  ['noAfectableManual', 'No afectable por asiento manual'], ['codigoAgrupador', 'Código agrupador'],
  ['grupoReporte', 'Grupo de reporte'], ['clase', 'Clase'], ['rubroId', 'Rubro'],
];

export function diferenciasCatalogo(cambio: CambioCuenta) {
  return CAMPOS.filter(([campo]) => !cambio.antes || cambio.antes[campo] !== cambio.despues[campo])
    .map(([campo, etiqueta]) => ({ campo, etiqueta, antes: cambio.antes?.[campo], despues: cambio.despues[campo] }));
}

export function valorDiferencia(valor: unknown): string {
  if (valor === undefined || valor === null || valor === '') return 'Sin valor';
  if (typeof valor === 'boolean') return valor ? 'Sí' : 'No';
  if (valor === 'Titulo') return 'Acumula';
  return String(valor);
}
