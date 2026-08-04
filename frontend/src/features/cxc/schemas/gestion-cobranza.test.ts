import { describe, expect, it } from 'vitest';
import { GestionCobranzaSchema } from '@/features/cxc/schemas/gestion-cobranza';
import { CanalCobranza, ResultadoCobranza } from '@/features/cxc/api/types';

const GUID = '018f6a5e-0000-7000-8000-000000000001';

const base = {
  clienteId: GUID,
  canal: CanalCobranza.Llamada,
  resultado: ResultadoCobranza.SinRespuesta,
  montoComprometido: null,
  fechaComprometida: null,
  nota: 'Se llamó al contacto de pagos; buzón.',
};

describe('GestionCobranzaSchema', () => {
  it('acepta gestión sin compromiso (SinRespuesta)', () => {
    expect(GestionCobranzaSchema.parse(base)).toEqual(base);
  });

  it('promesa de pago exige monto y fecha (espejo SC_PROMESA_*)', () => {
    const sinCompromiso = GestionCobranzaSchema.safeParse({
      ...base,
      resultado: ResultadoCobranza.PromesaPago,
    });
    expect(sinCompromiso.success).toBe(false);
    if (!sinCompromiso.success) {
      const paths = sinCompromiso.error.issues.map((i) => i.path[0]);
      expect(paths).toContain('montoComprometido');
      expect(paths).toContain('fechaComprometida');
    }

    const completa = GestionCobranzaSchema.safeParse({
      ...base,
      resultado: ResultadoCobranza.PromesaPago,
      montoComprometido: 25000,
      fechaComprometida: '2026-07-21',
    });
    expect(completa.success).toBe(true);
  });

  it('rechaza nota vacía y monto <= 0', () => {
    expect(GestionCobranzaSchema.safeParse({ ...base, nota: '' }).success).toBe(
      false,
    );
    expect(
      GestionCobranzaSchema.safeParse({
        ...base,
        resultado: ResultadoCobranza.PromesaPago,
        montoComprometido: 0,
        fechaComprometida: '2026-07-21',
      }).success,
    ).toBe(false);
  });
});
