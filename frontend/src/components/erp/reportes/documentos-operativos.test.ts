import { expect, it } from 'vitest';
import { reporteRequisicion } from '@/features/compras/lib/requisicion-impresion';
import { reporteConteo } from '@/features/activos-fijos/lib/conteo-impresion';
import { reporteHojaSalida } from '@/features/producto-terminado/lib/documentos-impresion';
import type { RequisicionResponse } from '@/features/compras/api/types';
import type { Conteo } from '@/features/activos-fijos/api/conservacion';
import type { HojaSalidaDto } from '@/features/producto-terminado/api/types';

it('la requisición conserva todas las partidas, moneda y etiquetas resueltas', () => {
  const rq = { folio: 'RQ-FICTICIA', estado: 0, prioridad: 1,
    lineas: Array.from({ length: 25 }, (_, i) => ({ posicion: i + 1, articuloId: 'a1', articuloClave: 'FIX', articuloNombre: 'Muestra', precioEstimadoMonto: 30, precioEstimadoMoneda: 'USD', cantidad: 2, centroCostoClave: 'CC', centroCostoNombre: 'Centro', proyecto: 'Obra' })),
  } as RequisicionResponse;
  const r = reporteRequisicion(rq, 'Sucursal ficticia');
  expect(r.filas).toHaveLength(25);
  expect(r.filas[24]).toMatchObject({ posicion: 25, articulo: 'FIX · Muestra', precio: '30 USD', centro: 'CC · Centro · Obra' });
  expect(r.filtrosAplicados.Sucursal).toBe('Sucursal ficticia');
});

it('el conteo cerrado conserva evidencias y autorizaciones de todas las partidas', () => {
  const c = { id: 'conteo-ficticio', sucursalId: 's1', createdAt: '2026-11-11', cerradoEn: '2026-11-11', ubicacion: null,
    partidas: Array.from({ length: 20 }, (_, i) => ({ id: String(i), numero: String(i), resultado: 1, autorizadoPor: 'Supervisor', evidenciaAutorizacion: 'Muestra autorizada', propuesta: 'Conservar' })),
  } as Conteo;
  const r = reporteConteo(c);
  expect(r.filas).toHaveLength(20);
  expect(r.filas[19]).toMatchObject({ numero: '19', autorizacion: 'Supervisor · Muestra autorizada · Conservar' });
});

it('la hoja conserva destino alterno, piezas rotas y receptor', () => {
  const h = { folio: 'HS-FICTICIA', sucursalId: 's1', destino: 'Destino de muestra', estado: 'Emitida', pedidos: 1, piezas: 2,
    albaranes: [{ destino: null, lineas: [{ posicion: 1, piezas: 2, descripcion: 'Vidrio ficticio' }], etiquetas: ['E1'], rotas: ['E2'], entrega: { resultado: 'Parcial', receptor: 'Persona ficticia' } }],
  } as HojaSalidaDto;
  const r = reporteHojaSalida(h, () => 'Sucursal ficticia');
  expect(r.filas[0]).toMatchObject({ destino: h.destino, entregaTexto: 'Parcial · recibió Persona ficticia' });
  expect(r.filas[0].cargado).toContain('Rotas pendientes: E2');
  expect(r.filas[0].cargado).toContain('2 pzas · partida 1 Vidrio ficticio');
});
