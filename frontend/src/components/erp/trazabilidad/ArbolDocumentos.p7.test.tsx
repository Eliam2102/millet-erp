import type { ReactNode } from 'react';
import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ArbolDocumentos } from './ArbolDocumentos';
import { TipoDocumentoTrazabilidad as T, type NodoArbolDocumento } from './types';
vi.mock('@tanstack/react-router', () => ({ Link: ({ children }: { children: ReactNode }) => <span>{children}</span> }));
function nodo(tipoDocumento: NodoArbolDocumento['tipoDocumento'], folio: string, descendentes: NodoArbolDocumento[] = []): NodoArbolDocumento {
  return { tipoDocumento, folio, id: folio, estado: 'DEMO', fecha: '2026-10-09T12:00:00Z', ascendentes: [], descendentes };
}
describe('P7 árbol recursivo', () => {
  it('muestra desde RQ la recepción, factura y pago de su OC', () => {
    const pago = nodo(T.PagoProveedor, 'PAGO-DEMO');
    const factura = nodo(T.FacturaProveedor, 'FACT-DEMO', [pago]);
    const recepcion = nodo(T.Recepcion, 'REC-DEMO', [factura]);
    const oc = nodo(T.OrdenCompra, 'OC-DEMO', [recepcion]);
    const rq = nodo(T.Requisicion, 'RQ-DEMO', [oc]);
    render(<ArbolDocumentos raiz={rq} tipoActual={T.Requisicion} idActual={rq.id} />);
    for (const folio of ['RQ-DEMO', 'OC-DEMO', 'REC-DEMO', 'FACT-DEMO', 'PAGO-DEMO']) expect(screen.getByText(folio)).toBeInTheDocument();
  });

  it('muestra la recepción cancelada junto a la vigente con la marca «Cancelada»', () => {
    const vigente = { ...nodo(T.Recepcion, 'REC-OK'), estado: 'Registrado' };
    const mala = { ...nodo(T.Recepcion, 'REC-MALA'), estado: 'Cancelado' };
    const oc = nodo(T.OrdenCompra, 'OC-DEMO', [mala, vigente]);
    render(<ArbolDocumentos raiz={oc} tipoActual={T.OrdenCompra} idActual={oc.id} />);
    expect(screen.getByText('REC-MALA')).toBeInTheDocument();
    expect(screen.getByText('REC-OK')).toBeInTheDocument();
    expect(screen.getAllByText('Cancelada')).toHaveLength(1);
    expect(screen.getByText('Registrado')).toBeInTheDocument();
  });
});
