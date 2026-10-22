import { describe, expect, it } from 'vitest';
import { textoSolicitudAw } from './solicitud-aw';

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
          { campo: 'NAME1', etiqueta: 'Nombre o razón social', regla: 'Obligatorio.', comoDebeQuedar: 'Razón social.' },
        ],
      },
      '22 oct 2026',
    );
    expect(t).toContain('Asunto: Corrección de datos de cliente en A+W · ref. 6682');
    expect(t).toContain('- NAME1 (Nombre o razón social): (vacío)');
    expect(t).toContain('- UST_ID (RFC): ABC010101AB1');
    expect(t).toContain('- NAME1 (Nombre o razón social): Obligatorio.');
    expect(t).toContain('- NAME1: Razón social.');
  });
});
