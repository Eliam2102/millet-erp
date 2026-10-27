import { describe, expect, it } from 'vitest';
import { ApiError } from '@/lib/api';
import {
  ComportamientoSerie,
  EstadoAutorizacionCredito,
  ReglaAplicadaLiberacion,
  ResultadoLiberacion,
  type AutorizacionCreditoResponse,
  type DecisionLiberacionResponse,
} from '@/features/cxc/api/types';
import {
  autorizacionesConsumibles,
  avisoResultadoAutorizar,
  calcularCobertura,
  comportamientoSeriePedido,
  mensajeErrorAutorizar,
  mostrarSelectorOverride,
  puedeAutorizarPendiente,
  textoCoberturaPreview,
  textoSinLineaActiva,
} from '@/features/cxc/lib/autorizar-pendiente';
import { formatoCobertura } from '@/features/cxc/lib/glosario';

const pendiente: DecisionLiberacionResponse = {
  id: 'dl-p',
  pedidoRef: '9000102',
  clienteId: 'cli-1',
  moneda: 'MXN',
  montoPedido: 60000,
  creditoDisponibleSnapshot: 40000,
  resultado: ResultadoLiberacion.PendienteAutorizacion,
  reglaAplicada: ReglaAplicadaLiberacion.Cobertura,
  overrideId: null,
  decididoPor: 'u-coord',
  decididoEn: '2026-10-25T09:00:00Z',
  porcentajeCobertura: 66.66,
  decisionOrigenId: null,
  resolucionId: null,
};

describe('calcularCobertura (C7.3, espejo de CoberturaCredito)', () => {
  it('CA7.2: 40,000 / 60,000 → 66.66 (trunca, no redondea)', () => {
    expect(calcularCobertura(40000, 60000)).toBe(66.66);
  });
  it('bordes 50 % exacto, 99.99 % y 100 %', () => {
    expect(calcularCobertura(50000, 100000)).toBe(50);
    expect(calcularCobertura(99990, 100000)).toBe(99.99);
    expect(calcularCobertura(99999.99, 100000)).toBe(99.99);
    expect(calcularCobertura(100000, 100000)).toBe(100);
  });
  it('CA7.3: 30 %; acota negativos a 0 y sin monto devuelve null', () => {
    expect(calcularCobertura(30000, 100000)).toBe(30);
    expect(calcularCobertura(-10, 100)).toBe(0);
    expect(calcularCobertura(10, 0)).toBeNull();
    expect(calcularCobertura(null, 100)).toBeNull();
  });
});

describe('formatoCobertura (supuesto b)', () => {
  it('muestra entero truncado', () => {
    expect(formatoCobertura(66.66)).toBe('Cobertura 66 %');
    expect(formatoCobertura(99.99)).toBe('Cobertura 99 %');
    expect(formatoCobertura(null)).toBeNull();
  });
});

describe('puedeAutorizarPendiente', () => {
  it('gerente con permiso override, distinto de quien decidió', () => {
    expect(puedeAutorizarPendiente(pendiente, true, 'u-nestor')).toBe(true);
  });
  it('sin autoconsumo: quien registró la pendiente no ve la acción', () => {
    expect(puedeAutorizarPendiente(pendiente, true, 'u-coord')).toBe(false);
  });
  it('sin permiso override, ya resuelta o no pendiente → false', () => {
    expect(puedeAutorizarPendiente(pendiente, false, 'u-nestor')).toBe(false);
    expect(puedeAutorizarPendiente({ ...pendiente, resolucionId: 'dl-r' }, true, 'u-nestor')).toBe(
      false,
    );
    expect(
      puedeAutorizarPendiente(
        { ...pendiente, resultado: ResultadoLiberacion.Retenido },
        true,
        'u-nestor',
      ),
    ).toBe(false);
  });
});

describe('mensajeErrorAutorizar', () => {
  const err = (status: number, code?: string) =>
    new ApiError({ type: 'about:blank', title: 'Title backend', status, code }, status);

  it('409 ya resuelta → mensaje en español', () => {
    expect(mensajeErrorAutorizar(err(409, 'DL_PENDIENTE_YA_RESUELTA')).titulo).toMatch(
      /ya fue resuelta \(autorizada o rechazada\)/,
    );
  });
  it('409 sin código conocido → recargar', () => {
    expect(mensajeErrorAutorizar(err(409)).titulo).toMatch(/recarga la bandeja/);
  });
  it('422 autoconsumo y cobertura insuficiente', () => {
    expect(mensajeErrorAutorizar(err(422, 'DL_AUTOCONSUMO')).titulo).toMatch(
      /no puede autorizarla/,
    );
    expect(mensajeErrorAutorizar(err(422, 'CXC_COBERTURA_INSUFICIENTE')).titulo).toMatch(
      /menos del 50 %/,
    );
  });
  it('otros 422 usan el title del backend', () => {
    expect(mensajeErrorAutorizar(err(422, 'OTRO')).titulo).toBe('Title backend');
  });
  it('C7.3 (e): sin línea activa con override', () => {
    expect(mensajeErrorAutorizar(err(422, 'CXC_SIN_LINEA_ACTIVA')).titulo).toMatch(
      /no procede autorización de CxC/,
    );
  });
  it('E10: autorización de otro pedido', () => {
    expect(mensajeErrorAutorizar(err(422, 'AC_REFERENCIA_DISTINTA')).titulo).toMatch(/otro pedido/);
  });
  it('rechazar: autoconsumo y errores genéricos en español', () => {
    expect(mensajeErrorAutorizar(err(422, 'DL_AUTOCONSUMO'), 'rechazar').titulo).toMatch(
      /ni rechazarla/,
    );
    expect(mensajeErrorAutorizar(err(403), 'rechazar').titulo).toMatch(/rechazar liberaciones/);
    expect(mensajeErrorAutorizar(new Error('x'), 'rechazar').titulo).toMatch(/al rechazar/);
  });
});

describe('avisoResultadoAutorizar (C7.3, recálculo al autorizar)', () => {
  it('el crédito ya alcanza → liberado por crédito', () => {
    const aviso = avisoResultadoAutorizar({
      ...pendiente,
      resultado: ResultadoLiberacion.Liberado,
      porcentajeCobertura: 100,
    });
    expect(aviso.tipo).toBe('success');
    expect(aviso.titulo).toMatch(/liberado por crédito/);
  });
  it('la cobertura bajó del 50 % → retenido', () => {
    const aviso = avisoResultadoAutorizar({
      ...pendiente,
      resultado: ResultadoLiberacion.Retenido,
      porcentajeCobertura: 33.33,
    });
    expect(aviso.tipo).toBe('warning');
    expect(aviso.detalle).toMatch(/bajó del 50 % \(33 %\)/);
  });
  it('sin línea activa → retenido', () => {
    const aviso = avisoResultadoAutorizar({
      ...pendiente,
      resultado: ResultadoLiberacion.Retenido,
      porcentajeCobertura: null,
    });
    expect(aviso.tipo).toBe('warning');
    expect(aviso.detalle).toMatch(/línea de crédito activa/);
  });
  it('sigue parcial → liberado con autorización', () => {
    const aviso = avisoResultadoAutorizar({
      ...pendiente,
      resultado: ResultadoLiberacion.LiberadoConOverride,
    });
    expect(aviso.tipo).toBe('success');
    expect(aviso.titulo).toMatch(/liberado con autorización/);
  });
});

const autorizacion = (
  id: string,
  vigenteHasta: string,
  estado: EstadoAutorizacionCredito = EstadoAutorizacionCredito.Autorizada,
): AutorizacionCreditoResponse => ({
  id,
  supervisorUsuarioId: 'u-gerente',
  beneficiarioUsuarioId: 'u-coord',
  motivo: 'Cliente al corriente',
  clienteOPedidoRef: id,
  fechaAutorizacion: '2026-10-25T08:00:00Z',
  vigenteHasta,
  estado,
  decisionLiberacionId: null,
  version: 1,
});

describe('autorizacionesConsumibles (C7.3, hallazgo QA 1)', () => {
  const ahora = new Date('2026-10-25T12:00:00Z');

  it('deja solo las autorizadas con vigencia futura', () => {
    const items = [
      autorizacion('QA-VIGENTE', '2026-10-25T20:00:00Z'),
      autorizacion('QA-C73-VENC', '2026-10-24T12:00:00Z'),
      autorizacion('QA-USADA', '2026-10-25T20:00:00Z', EstadoAutorizacionCredito.Usada),
    ];
    expect(autorizacionesConsumibles(items, ahora).map((a) => a.id)).toEqual(['QA-VIGENTE']);
  });

  it('una autorización que vence justo ahora ya no se ofrece', () => {
    expect(
      autorizacionesConsumibles([autorizacion('QA-LIMITE', ahora.toISOString())], ahora),
    ).toEqual([]);
  });
});

describe('mostrarSelectorOverride (C7.3, hallazgo QA 2 y supuesto d)', () => {
  it('no se ofrece si el crédito alcanza', () => {
    expect(mostrarSelectorOverride(true, 100)).toBe(false);
  });

  it('se ofrece con cobertura de 50 % a 99 %', () => {
    expect(mostrarSelectorOverride(false, 50)).toBe(true);
    expect(mostrarSelectorOverride(false, 66.66)).toBe(true);
  });

  it('no se ofrece con cobertura menor a 50 %', () => {
    expect(mostrarSelectorOverride(false, 49.99)).toBe(false);
    expect(mostrarSelectorOverride(false, 30)).toBe(false);
  });

  it('C7.3 (e): sin línea activa en la moneda (cobertura null) ya no se ofrece', () => {
    expect(mostrarSelectorOverride(false, null)).toBe(false);
    expect(mostrarSelectorOverride(false, null, ComportamientoSerie.EvaluaCredito)).toBe(false);
  });
});

describe('serie que exige autorización (C7.3, sin regresión de C7.2)', () => {
  const reglas = [
    { prefijo: '3000', comportamiento: ComportamientoSerie.NuncaLibera },
    { prefijo: '5000', comportamiento: ComportamientoSerie.SiempreLibera },
    { prefijo: '30009', comportamiento: ComportamientoSerie.SiempreLibera },
  ];

  it('resuelve la serie por el prefijo activo más largo, como el backend', () => {
    expect(comportamientoSeriePedido(' 3000123456 ', reglas)).toBe(ComportamientoSerie.NuncaLibera);
    expect(comportamientoSeriePedido('3000999', reglas)).toBe(ComportamientoSerie.SiempreLibera);
    expect(comportamientoSeriePedido('QA-C73-01', reglas)).toBe(ComportamientoSerie.EvaluaCredito);
    expect(comportamientoSeriePedido('3000123456', [])).toBe(ComportamientoSerie.EvaluaCredito);
  });

  it('serie 3000/4000/8000 muestra el selector aunque la cobertura sea menor a 50 %', () => {
    expect(mostrarSelectorOverride(false, 30, ComportamientoSerie.NuncaLibera)).toBe(true);
    expect(mostrarSelectorOverride(false, 0, ComportamientoSerie.NuncaLibera)).toBe(true);
  });

  it('serie que exige autorización sin línea sigue mostrando el selector', () => {
    expect(mostrarSelectorOverride(false, null, ComportamientoSerie.NuncaLibera)).toBe(true);
  });

  it('serie que exige autorización muestra el selector aunque el crédito alcance', () => {
    expect(mostrarSelectorOverride(true, 100, ComportamientoSerie.NuncaLibera)).toBe(true);
  });

  it('pedido que evalúa crédito con cobertura menor a 50 % sigue sin selector', () => {
    expect(mostrarSelectorOverride(false, 30, ComportamientoSerie.EvaluaCredito)).toBe(false);
  });
});

describe('textoCoberturaPreview (C7.3, observación QA 4)', () => {
  it('serie que exige autorización con cobertura < 50 % no dice «no procede autorización»', () => {
    const texto = textoCoberturaPreview(false, false, true);
    expect(texto).not.toMatch(/no procede autorización/);
    expect(texto).toMatch(/no se libera por crédito/);
  });

  it('serie que exige autorización con crédito suficiente tampoco dice que alcanza', () => {
    expect(textoCoberturaPreview(true, false, true)).not.toMatch(/alcanza/);
  });

  it('pedido que evalúa crédito conserva los tres textos de cobertura', () => {
    expect(textoCoberturaPreview(true, false, false)).toMatch(/alcanza/);
    expect(textoCoberturaPreview(false, true, false)).toMatch(/pendiente de autorización/);
    expect(textoCoberturaPreview(false, false, false)).toMatch(/no procede autorización de CxC/);
  });
});

describe('textoSinLineaActiva (C7.3, supuesto e)', () => {
  it('serie que evalúa crédito: retenido sin autorización de CxC', () => {
    const texto = textoSinLineaActiva('ACME', 'MXN', false, false);
    expect(texto).toMatch(/no tiene línea de crédito activa en MXN/);
    expect(texto).toMatch(/no procede autorización de CxC/);
    expect(texto).not.toMatch(/override/);
  });
  it('línea bloqueada se nombra como bloqueada', () => {
    expect(textoSinLineaActiva('ACME', 'USD', true, false)).toMatch(/en USD bloqueada/);
  });
  it('serie que exige autorización conserva el override', () => {
    const texto = textoSinLineaActiva('ACME', 'MXN', false, true);
    expect(texto).toMatch(/sin override, la decisión quedará retenida/);
    expect(texto).not.toMatch(/no procede/);
  });
});
