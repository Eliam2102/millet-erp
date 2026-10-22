import { describe, expect, it } from 'vitest';
import { textoSolicitudAw, textoSolicitudesPorArea } from './solicitud-aw';

describe('textoSolicitudAw', () => {
  it('incluye cómo llegó, qué falta y cómo debe quedar; vacío se escribe explícito', () => {
    const t = textoSolicitudAw(
      {
        referencia: '6682',
        campos: [
          { campo: 'NAME1', etiqueta: 'Nombre o razón social', valor: null },
          { campo: 'UST_ID', etiqueta: 'RFC', valor: 'ABC010101AB1' },
        ],
        faltantes: [
          { campo: 'NAME1', etiqueta: 'Nombre o razón social', regla: 'Obligatorio.', comoDebeQuedar: 'Razón social.', area: 'Fiscal', corrigeEn: 'A+W' },
        ],
      },
      '22 oct 2026',
    );
    expect(t).toContain('Asunto: Corrección de datos de cliente en A+W · ref. 6682');
    expect(t).toContain('- NAME1 (Nombre o razón social): (vacío)');
    expect(t).toContain('- UST_ID (RFC): ABC010101AB1');
    expect(t).toContain('- NAME1 (Nombre o razón social): Obligatorio. Valida: Fiscal; se corrige en A+W.');
    expect(t).toContain('- NAME1: Razón social.');
  });
});

describe('textoSolicitudesPorArea', () => {
  it('agrupa por área con su siguiente paso y cada referencia', () => {
    const fila = (area: string, referencia: string, corrigeEn: string) => ({
      area, corrigeEn, referencia, paso: `paso ${area}`, codigo: 'fila_invalida',
      error: null, comoLlego: null, queFalta: `falta ${referencia}`, comoDebeQuedar: null,
    });
    const t = textoSolicitudesPorArea({
      titulo: 't', generadoEn: '', filtrosAplicados: {}, columnas: [], totales: {}, truncado: false,
      filas: [fila('Fiscal', '1', 'A+W'), fila('Fiscal', '2', 'A+W'), fila('TI', '3', 'A+W')],
    });
    expect(t).toContain('Para: Fiscal (2 solicitudes)');
    expect(t).toContain('Siguiente paso: paso Fiscal');
    expect(t).toContain('- Ref. 2: falta 2 | Se corrige en: A+W');
    expect(t).toContain('Para: TI (1 solicitud)');
  });
});
