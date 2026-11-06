/** Nombre visible de la clave de módulo que manda el servidor (`almacen`, `cuentas_por_pagar`…). */
const NOMBRES: Record<string, string> = {
  almacen: 'Almacén',
  compras: 'Compras',
  cuentas_por_pagar: 'CxP',
  cuentas_por_cobrar: 'CxC',
  datos_maestros: 'Datos maestros',
  facturacion: 'Facturación',
  tesoreria: 'Tesorería',
  contabilidad: 'Contabilidad',
  comercio_exterior: 'Comercio exterior',
  activos_fijos: 'Activos fijos',
  producto_terminado: 'Producto terminado',
};

export function nombreModulo(clave: string): string {
  return NOMBRES[clave] ?? clave.replace(/[_-]+/g, ' ').replace(/^./, (c) => c.toUpperCase());
}
