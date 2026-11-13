import { expect, it } from 'vitest';
import type { Poliza } from '../api/polizas-types';
import type { PlantillaRecurrente } from '../api/recurrentes-types';
import { poliza } from './__fixtures__/polizas-fix';
import { plantilla } from './__fixtures__/recurrentes-fix';
import { reportePlantilla, reportePoliza } from './documentos-impresion';

it('conserva partidas y cuadre de la póliza, con su estado y autorización', () => {
  const p = poliza() as Poliza;
  const reporte = reportePoliza(p);
  expect(reporte.filas).toHaveLength(p.partidas.length);
  expect(reporte.filas[0].cuenta).toContain(p.partidas[0].cuentaCodigo);
  expect(reporte.totales).toEqual({ cargo: p.totalCargos, abono: p.totalAbonos });
  expect(reporte.filtrosAplicados).toMatchObject({ Estado: p.estado, Autorizó: p.autorizadaPor });
});

it('resuelve etiquetas de la plantilla y conserva calendario, pendientes y reversa', () => {
  const p = plantilla() as PlantillaRecurrente;
  const reporte = reportePlantilla(p, id => `Cuenta ${id}`, id => `Sucursal ${id}`);
  expect(reporte.filas).toHaveLength(2);
  expect(reporte.filas[0]).toMatchObject({ cuenta: 'Cuenta c1', sucursal: 'Sucursal s1' });
  expect(reporte.notas?.join("; ")).toContain('TT-000130');
  expect(reporte.notas?.join("; ")).toContain('Reversa TT-000131');
  expect(reporte.notas?.join("; ")).toContain('Pendiente');
  expect(reporte.totales).toEqual({ cargo: 5000, abono: 5000 });
});
