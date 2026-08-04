import { describe, expect, it } from 'vitest';
import {
  ESTADOS,
  ESTADOS_OC,
  NATURALEZAS,
  TRANSVERSALES,
  obtenerDefinicion,
  obtenerDefinicionOc,
} from '@/features/compras/lib/glosario';

describe('glosario de Compras', () => {
  it('cubre los 10 estados del agregado Requisicion', () => {
    const estados = Object.keys(ESTADOS);
    expect(estados).toHaveLength(10);
    expect(estados).toEqual(
      expect.arrayContaining([
        'Borrador',
        'EnAutorizacion',
        'Autorizada',
        'EnSurtido',
        'Cerrada',
        'Cancelada',
        'Rechazada',
        'Eliminada',
        'CerradaSinSurtir',
        'CerradaSurtidaParcial',
      ]),
    );
  });

  it('cubre las 4 naturalezas del artículo', () => {
    expect(Object.keys(NATURALEZAS)).toEqual(
      expect.arrayContaining(['Estandar', 'Servicio', 'Critico', 'Riesgo']),
    );
    expect(Object.keys(NATURALEZAS)).toHaveLength(4);
  });

  it('cubre los términos transversales (RQ + OC)', () => {
    const transversales = Object.keys(TRANSVERSALES);
    // RQ
    expect(transversales).toEqual(
      expect.arrayContaining([
        'Cubrimiento',
        'Matriz',
        'Bifurcacion',
        'Reserva',
      ]),
    );
    // OC (UF0-PR1: 11 términos del 06-frontend-plan §Fase 0)
    expect(transversales).toEqual(
      expect.arrayContaining([
        'SubEstado',
        'PartidaAbierta',
        'Consolidacion',
        'DuplicarOc',
        'CotizacionExcepcionada',
        'OcOrigen',
        'Contenedor',
        'Ruta',
        'Semana',
        'Pedimento',
        'Incoterm',
      ]),
    );
    // Total: 4 (RQ) + 11 (OC) = 15.
    expect(transversales).toHaveLength(15);
  });

  it('todos los resúmenes son ≤ 300 caracteres (margen sobre 200)', () => {
    const todos = [
      ...Object.values(ESTADOS),
      ...Object.values(ESTADOS_OC),
      ...Object.values(NATURALEZAS),
      ...Object.values(TRANSVERSALES),
    ];
    for (const def of todos) {
      expect(def.resumen.length).toBeLessThanOrEqual(300);
    }
  });

  it('obtenerDefinicion resuelve estados', () => {
    expect(obtenerDefinicion('EnSurtido')?.resumen).toContain('autorizada');
  });

  it('obtenerDefinicion resuelve naturalezas', () => {
    expect(obtenerDefinicion('Critico')?.resumen).toContain('Nivel 2');
  });

  it('obtenerDefinicion resuelve transversales', () => {
    expect(obtenerDefinicion('Cubrimiento')).toBeDefined();
  });

  it('obtenerDefinicion devuelve undefined para términos desconocidos', () => {
    expect(obtenerDefinicion('NoExiste')).toBeUndefined();
  });
});

describe('glosario OC (UF0-PR1)', () => {
  it('cubre los 7 estados del agregado OrdenCompra', () => {
    const estados = Object.keys(ESTADOS_OC);
    expect(estados).toHaveLength(7);
    expect(estados).toEqual(
      expect.arrayContaining([
        'Borrador',
        'EnAutorizacionJefeCompras',
        'EnAutorizacionDireccion',
        'Autorizada',
        'Cerrada',
        'Cancelada',
        'Rechazada',
      ]),
    );
  });

  it('obtenerDefinicionOc resuelve estados OC con wording propio', () => {
    // Para `Autorizada`, RQ habla de "almacén o compras la materialicen"
    // y OC habla de "transmitió al proveedor". Verificamos que la
    // variante OC devuelve el wording de OC, no el de RQ.
    const ocAutorizada = obtenerDefinicionOc('Autorizada')?.resumen ?? '';
    const rqAutorizada = obtenerDefinicion('Autorizada')?.resumen ?? '';
    expect(ocAutorizada).toContain('proveedor');
    expect(rqAutorizada).not.toContain('proveedor');
    expect(ocAutorizada).not.toBe(rqAutorizada);
  });

  it('obtenerDefinicionOc resuelve los términos transversales OC', () => {
    expect(obtenerDefinicionOc('Consolidacion')?.resumen).toContain(
      'mismo proveedor',
    );
    expect(obtenerDefinicionOc('DuplicarOc')?.resumen).toContain(
      'cancelar',
    );
    expect(obtenerDefinicionOc('Pedimento')?.resumen).toContain('aduanal');
    expect(obtenerDefinicionOc('Incoterm')?.resumen).toContain('FOB');
  });

  it('obtenerDefinicionOc no resuelve estados exclusivos de RQ', () => {
    // `EnSurtido` y `Eliminada` son estados exclusivos de RQ; OC no los
    // tiene en ESTADOS_OC y `obtenerDefinicionOc` no toca el dict de RQ.
    expect(obtenerDefinicionOc('EnSurtido')).toBeUndefined();
    expect(obtenerDefinicionOc('Eliminada')).toBeUndefined();
  });

  it('obtenerDefinicionOc sí resuelve transversales compartidos (TRANSVERSALES es único cross-submódulo)', () => {
    // Decisión UF0-PR1: TRANSVERSALES es un único diccionario que mezcla
    // términos de RQ (Cubrimiento, Matriz, ...) y de OC (Consolidacion,
    // Pedimento, ...). `obtenerDefinicionOc` lo consulta entero — un
    // tooltip OC que mencione `Cubrimiento` en el futuro no debería
    // romperse por esto.
    expect(obtenerDefinicionOc('Cubrimiento')).toBeDefined();
    expect(obtenerDefinicionOc('Matriz')).toBeDefined();
  });

  it('obtenerDefinicion (legacy) prefiere el diccionario RQ para claves compartidas', () => {
    // Documentado: `Borrador` existe en ambos; obtenerDefinicion() devuelve
    // la versión RQ. Quien necesite la versión OC debe usar
    // obtenerDefinicionOc().
    const borradorLegacy = obtenerDefinicion('Borrador')?.resumen ?? '';
    const borradorOc = obtenerDefinicionOc('Borrador')?.resumen ?? '';
    expect(borradorLegacy).toContain('requisición');
    expect(borradorOc).toContain('OC');
  });
});
