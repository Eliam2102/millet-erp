import { describe, expect, it } from 'vitest';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { rutaPermitida } from '@/lib/nav';
import { pendienteVisible, pendientes, type PendienteConfig } from './config';
const requeridos = [P.ComprasRequisicionesLeer, P.ComprasOrdenesLeer];
const fila: PendienteConfig = {
  id: 'prueba',
  modulo: 'Compras',
  titulo: 'Prueba',
  descripcion: '',
  to: '/compras/pendientes',
  conteo: 'requisiciones',
  permisos: requeridos,
  permisosAlguno: [P.ComprasRequisicionesAutorizarNivel1, P.ComprasRequisicionesAutorizarNivel2],
};
describe('pendienteVisible', () => {
  it.each([
    { permisos: [], visible: false },
    { permisos: requeridos, visible: false },
    { permisos: [P.ComprasRequisicionesAutorizarNivel1], visible: false },
    {
      permisos: [P.ComprasRequisicionesLeer, P.ComprasRequisicionesAutorizarNivel1],
      visible: false,
    },
    { permisos: [P.ComprasOrdenesLeer, P.ComprasRequisicionesAutorizarNivel1], visible: false },
    { permisos: [...requeridos, P.ComprasRequisicionesAutorizarNivel1], visible: true },
    { permisos: [...requeridos, P.ComprasRequisicionesAutorizarNivel2], visible: true },
    {
      permisos: [
        ...requeridos,
        P.ComprasRequisicionesAutorizarNivel1,
        P.ComprasRequisicionesAutorizarNivel2,
      ],
      visible: true,
    },
  ])(
    'exige todos los requeridos y al menos uno alternativo: $permisos → $visible',
    ({ permisos, visible }) => {
      expect(pendienteVisible(fila, permisos)).toBe(visible);
    },
  );
  it.each(pendientes.filter((item) => !item.conteo && !item.periodo))(
    'acceso sin número $id depende de rutaPermitida',
    (acceso) => {
      expect(acceso.permisos).toBeUndefined();
      expect(acceso.permisosAlguno).toBeUndefined();
      expect(rutaPermitida(acceso.to, [])).toBe(false);
      expect(pendienteVisible(acceso, [])).toBe(false);
      const permisos = Object.values(P);
      expect(rutaPermitida(acceso.to, permisos)).toBe(true);
      expect(pendienteVisible(acceso, permisos)).toBe(true);
    },
  );
  it('rechaza ruta no permitida aunque cumpla permisos explícitos', () => {
    const permisos = [...requeridos, P.ComprasRequisicionesAutorizarNivel1];
    const restringida = { ...fila, to: '/cxp/facturas' };
    expect(rutaPermitida(restringida.to, permisos)).toBe(false);
    expect(pendienteVisible(restringida, permisos)).toBe(false);
  });
});
