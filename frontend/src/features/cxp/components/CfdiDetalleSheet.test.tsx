import { beforeEach, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { CfdiListItem } from '@/features/cxp/api/types';
import { CfdiDetalleSheet } from './CfdiDetalleSheet';

const datos = vi.hoisted(() => ({ rfcEmisor: 'DEMO010101AAA', subtotal: 100 as number | null | undefined }));
vi.mock('@/features/cxp/api/useCfdis', () => ({
  useCfdiDetalle: () => ({ data: {
    id: 'cfdi-demo', uuidCfdi: 'uuid-demo', rfcEmisor: datos.rfcEmisor, tipo: 1, serie: 'A', folio: '123',
    fechaCfdi: '2026-11-02T00:00:00Z', fechaRecepcion: '2026-11-03T12:00:00Z',
    canalOrigen: 3, subtotal: datos.subtotal, impuestosTrasladados: 16, retenciones: 0, total: 116, moneda: 'MXN',
    tipoCambio: null, estado: 1, estatusSat: null, tieneXml: true, tienePdf: false,
  } }),
  useCfdiParseado: () => ({ data: { lineas: [{ posicion: 1, claveProdServ: '30171700', descripcion: 'Vidrio de prueba', cantidad: 2, unidad: 'Pieza', valorUnitario: 50, importe: 100 }] } }),
  useCfdiPdfUrl: () => ({}),
}));
vi.mock('@/features/cxp/components/SolicitudesCancelacionPanel', () => ({ SolicitudesCancelacionPanel: () => null }));
beforeEach(() => { datos.rfcEmisor = 'DEMO010101AAA'; datos.subtotal = 100; });
const abrir = () => render(<CfdiDetalleSheet open onOpenChange={vi.fn()} cfdi={{ id: 'cfdi-demo', uuidCfdi: 'uuid-demo' } as CfdiListItem} />);

it('formatea las fechas conservando la fecha local anterior y muestra los precios con moneda', () => {
  abrir();
  expect(screen.getByText('1 nov 2026')).toBeInTheDocument();
  expect(screen.getByText('3 nov 2026')).toBeInTheDocument();
  expect(screen.getByText('$50.00')).toHaveClass('tabular-nums', { exact: false });
  expect(screen.getAllByText('$100.00')).toHaveLength(2);
  expect(screen.queryByText('2026-11-02')).not.toBeInTheDocument();
});

it('un campo vacío se presenta como «—» con texto apagado', () => {
  datos.rfcEmisor = '';
  abrir();
  const valor = screen.getByText('RFC emisor').nextElementSibling;
  expect(valor).toHaveTextContent('—');
  expect(valor).toHaveClass('text-ink-muted');
});

it.each([null, undefined, NaN])('un monto %s se muestra como «—» sin inventar cero', (valor) => {
  datos.subtotal = valor;
  abrir();
  expect(screen.getByText('Subtotal').nextElementSibling).toHaveTextContent('—');
  expect(screen.getByText('Subtotal').nextElementSibling).toHaveClass('text-ink-muted');
});
it('un monto cero explícito sí se muestra como dinero', () => {
  datos.subtotal = 0;
  abrir();
  expect(screen.getByText('Subtotal').nextElementSibling).toHaveTextContent('$0.00');
});
