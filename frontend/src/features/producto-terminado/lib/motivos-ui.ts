/** Textos en español de los códigos de motivo de un escaneo no resuelto. */
const motivos: Record<string, string> = {
  PT_PEDIDO_INEXISTENTE: 'El pedido todavía no está en Facturación',
  PT_PEDIDO_CANCELADO: 'El pedido está cancelado',
  PT_POSICION_INEXISTENTE: 'La posición no existe en el pedido',
  PT_PIEZA_EXCEDE_POSICION: 'El número de pieza excede las piezas de la posición',
  PT_CANTIDAD_NO_ENTERA: 'La cantidad de la posición no es un número entero de piezas',
  PT_SUCURSAL_SIN_CLAVE_AW: 'La sucursal del pedido no tiene clave A+W configurada',
  PT_IDENTIDAD_INCOMPLETA: 'El renglón no trae pedido, posición o número de pieza',
  PT_PIEZA_DUPLICADA: 'Ya hay otra pieza en ese lugar con otra etiqueta',
  PT_REPOSICION: 'Etiqueta repetida: posible pieza repuesta tras una rotura',
};

/** Código desconocido: se muestra tal cual. Sin motivo: guion. */
export function textoMotivo(codigo: string | null): string {
  if (!codigo) return '—';
  return motivos[codigo] ?? codigo;
}
