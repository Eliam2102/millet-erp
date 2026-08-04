import { describe, expect, it } from 'vitest';
import {
  OperadorSchema,
  VehiculoSchema,
} from '@/features/facturacion/schemas/carta-porte-catalogos';

const vehiculoValido = {
  placa: 'ABC-123',
  configVehicular: 'C2R2',
  anioModelo: 2022,
  pesoBrutoVehicular: 17.5,
  tipoPermisoSct: 'TPAF01',
  numPermisoSct: 'PERM-1',
  aseguradora: 'Seguros X',
  polizaSeguro: 'POL-1',
};

describe('VehiculoSchema', () => {
  it('acepta un vehículo válido', () => {
    expect(VehiculoSchema.safeParse(vehiculoValido).success).toBe(true);
  });

  it('acepta peso bruto vacío (null) y opcionales vacíos', () => {
    expect(
      VehiculoSchema.safeParse({
        placa: 'XYZ-987',
        configVehicular: 'VL',
        anioModelo: 2020,
        pesoBrutoVehicular: null,
        tipoPermisoSct: '',
        numPermisoSct: '',
        aseguradora: '',
        polizaSeguro: '',
      }).success,
    ).toBe(true);
  });

  it('exige placa y configuración vehicular', () => {
    expect(
      VehiculoSchema.safeParse({ ...vehiculoValido, placa: '  ' }).success,
    ).toBe(false);
    expect(
      VehiculoSchema.safeParse({ ...vehiculoValido, configVehicular: '' })
        .success,
    ).toBe(false);
  });

  it('exige año modelo entre 1990 y 2100', () => {
    expect(
      VehiculoSchema.safeParse({ ...vehiculoValido, anioModelo: 1980 }).success,
    ).toBe(false);
    expect(
      VehiculoSchema.safeParse({ ...vehiculoValido, anioModelo: 2150 }).success,
    ).toBe(false);
  });

  it('rechaza peso bruto <= 0 (toneladas — CP 3.1)', () => {
    expect(
      VehiculoSchema.safeParse({ ...vehiculoValido, pesoBrutoVehicular: 0 })
        .success,
    ).toBe(false);
    expect(
      VehiculoSchema.safeParse({ ...vehiculoValido, pesoBrutoVehicular: -1 })
        .success,
    ).toBe(false);
  });
});

const operadorValido = {
  rfc: 'OPER010101AAA',
  nombre: 'Juan Pérez',
  numLicencia: 'LIC-12345',
};

describe('OperadorSchema', () => {
  it('acepta un operador válido', () => {
    expect(OperadorSchema.safeParse(operadorValido).success).toBe(true);
  });

  it('exige RFC de 12 o 13 caracteres', () => {
    expect(
      OperadorSchema.safeParse({ ...operadorValido, rfc: 'CORTO' }).success,
    ).toBe(false);
    expect(
      OperadorSchema.safeParse({ ...operadorValido, rfc: 'DEMASIADOLARGO1' })
        .success,
    ).toBe(false);
  });

  it('exige nombre y número de licencia', () => {
    expect(
      OperadorSchema.safeParse({ ...operadorValido, nombre: ' ' }).success,
    ).toBe(false);
    expect(
      OperadorSchema.safeParse({ ...operadorValido, numLicencia: '' }).success,
    ).toBe(false);
  });
});
