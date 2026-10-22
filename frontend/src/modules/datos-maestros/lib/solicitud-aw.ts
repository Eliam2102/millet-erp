import type { ClienteAwOrigenDetalle } from '@/modules/datos-maestros/api/types';

/** Texto plano listo para pedir al equipo de A+W que corrija un registro que no se pudo importar. */
export function textoSolicitudAw(d: ClienteAwOrigenDetalle, fecha: string): string {
  const v = (x: string | null) => x ?? '(vacío)';
  return [
    `Asunto: Corrección de datos de cliente en A+W · ref. ${d.referencia}`,
    '',
    'Hola equipo A+W:',
    '',
    `En la sincronización del ERP del ${fecha} el cliente con referencia ${d.referencia} no se pudo importar porque al registro le faltan datos obligatorios.`,
    '',
    'Cómo llegó desde A+W:',
    ...d.campos.map((c) => `- ${c.campo} (${c.etiqueta}): ${v(c.valor)}`),
    '',
    'Qué falta o está mal:',
    ...d.faltantes.map((f) => `- ${f.campo} (${f.etiqueta}): ${f.regla}`),
    '',
    'Cómo debe quedar:',
    ...d.faltantes.map((f) => `- ${f.campo}: ${f.comoDebeQuedar}`),
    '',
    'Cuando lo corrijan avísennos para reintentar la lectura. Gracias.',
  ].join('\n');
}
