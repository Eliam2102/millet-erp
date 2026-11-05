import { describe, expect, it } from 'vitest';
import { RegistrarSalidaConRqSchema, RegistrarSalidaPorValeSchema } from './salida';

/** G-10: una salida no se confirma sin destinatario ni acuse; un vale tampoco sin motivo de la urgencia. */

const persona = '019e0000-0000-7000-8000-0000000000aa';
const rq = {
  requisicionId: '019e0000-0000-7000-8000-0000000000bb',
  fechaMovimiento: '2026-11-05',
  personaDestinatariaId: persona,
  acuseRecibeNombre: 'Auxiliar de mantenimiento',
  filas: [
    {
      articuloId: '019e0000-0000-7000-8000-0000000000cc',
      lineaRqId: '019e0000-0000-7000-8000-0000000000dd',
      incluida: true,
      cantidad: 1,
      cantidadSolicitada: 1,
      cantidadPendiente: 1,
      maximo: 1,
      unidadMedida: 'PZA',
      ubicacionId: '019e0000-0000-7000-8000-0000000000ee',
    },
  ],
};
const mensajes = (r: { success: boolean; error?: { issues: { message: string }[] } }) =>
  r.success ? [] : (r.error?.issues.map((i) => i.message) ?? []);

describe('salida con RQ — destinatario y acuse (G-10)', () => {
  it('sin acuse o sin destinatario no pasa', () => {
    expect(mensajes(RegistrarSalidaConRqSchema.safeParse({ ...rq, acuseRecibeNombre: '  ' }))).toContain('Captura el nombre de quien recibe.');
    expect(mensajes(RegistrarSalidaConRqSchema.safeParse({ ...rq, personaDestinatariaId: null }))).toContain('Elige el destinatario de la salida.');
  });

  it('con destinatario y acuse pasa', () => {
    const r = RegistrarSalidaConRqSchema.safeParse(rq);
    expect(mensajes(r)).not.toContain('Captura el nombre de quien recibe.');
    expect(mensajes(r)).not.toContain('Elige el destinatario de la salida.');
  });
});

describe('vale urgente — motivo, destinatario y acuse (G-10)', () => {
  const vale = {
    fechaMovimiento: '2026-11-05',
    valeBlobRef: 'blob://vales/vale-0001.pdf',
    personaDestinatariaId: persona,
    acuseRecibeNombre: 'Auxiliar de mantenimiento',
    motivoUrgencia: 'Paro de línea 2 por falla de sellador',
    lineas: [
      {
        articuloId: '019e0000-0000-7000-8000-0000000000cc',
        cantidad: 1,
        ubicacionId: '019e0000-0000-7000-8000-0000000000ee',
      },
    ],
  };

  it('sin motivo de la urgencia no pasa', () => {
    expect(mensajes(RegistrarSalidaPorValeSchema.safeParse({ ...vale, motivoUrgencia: '   ' }))).toContain('Indica el motivo de la urgencia.');
  });

  it('sin acuse o sin destinatario no pasa; completo pasa', () => {
    expect(mensajes(RegistrarSalidaPorValeSchema.safeParse({ ...vale, acuseRecibeNombre: '' }))).toContain('Captura el nombre de quien recibe.');
    expect(mensajes(RegistrarSalidaPorValeSchema.safeParse({ ...vale, personaDestinatariaId: undefined }))).toContain('Elige el destinatario de la salida.');
    expect(RegistrarSalidaPorValeSchema.safeParse(vale).success).toBe(true);
  });
});
