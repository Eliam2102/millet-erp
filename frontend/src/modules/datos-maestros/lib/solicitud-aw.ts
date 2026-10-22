import type { ReporteDiferenciasAw } from '@/modules/datos-maestros/api/sincronizacion-aw';
import type { ClienteAwOrigenDetalle } from '@/modules/datos-maestros/api/types';

/** Un bloque de texto por área (Fiscal, TI, Soporte VILO…) con sus referencias, listo para el correo. */
export function textoSolicitudesPorArea(r: ReporteDiferenciasAw): string {
  const porArea = new Map<string, ReporteDiferenciasAw['filas']>();
  for (const f of r.filas) {
    const area = f.area ?? 'Sin área';
    porArea.set(area, [...(porArea.get(area) ?? []), f]);
  }
  return [...porArea.entries()]
    .map(([area, filas]) =>
      [
        `Para: ${area} (${filas.length} ${filas.length === 1 ? 'solicitud' : 'solicitudes'})`,
        `Siguiente paso: ${filas[0]?.paso ?? ''}`,
        ...filas.map(
          (f) =>
            `- Ref. ${f.referencia}: ${f.queFalta ?? f.codigo}` +
            (f.comoLlego ? ` | Cómo llegó: ${f.comoLlego}` : '') +
            (f.comoDebeQuedar ? ` | Cómo debe quedar: ${f.comoDebeQuedar}` : '') +
            ` | Se corrige en: ${f.corrigeEn}`,
        ),
      ].join('\n'),
    )
    .join('\n\n');
}

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
    ...d.faltantes.map(
      (f) => `- ${f.campo} (${f.etiqueta}): ${f.regla} Valida: ${f.area}; se corrige en ${f.corrigeEn}.`,
    ),
    '',
    'Cómo debe quedar:',
    ...d.faltantes.map((f) => `- ${f.campo}: ${f.comoDebeQuedar}`),
    '',
    'Cuando lo corrijan avísennos para reintentar la lectura. Gracias.',
  ].join('\n');
}
