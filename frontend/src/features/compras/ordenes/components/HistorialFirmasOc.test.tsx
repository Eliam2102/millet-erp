import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { OrdenCompraDetalleResponse } from '../api/types';
import { HistorialFirmasOc } from './HistorialFirmasOc';

describe('Historial de firmas de la OC', () => {
  it('muestra los dos ciclos, los firmantes, fechas, rechazo y motivo', () => {
    const oc = {
      cicloAutorizacion: 2,
      autorizaciones: [
        { id: '1', ciclo: 1, nivel: 1, resultado: 1, usuarioId: 'jefe', fechaHora: '2026-10-09T12:00:00Z', motivoRechazoId: null, motivoRechazoTexto: null, notas: null },
        { id: '2', ciclo: 1, nivel: 2, resultado: 2, usuarioId: 'direccion', fechaHora: '2026-10-09T13:00:00Z', motivoRechazoId: 'motivo', motivoRechazoNombre: 'Precio incorrecto', motivoRechazoTexto: 'Corregir precio', notas: 'Revisar cotización' },
        { id: '3', ciclo: 2, nivel: 1, resultado: 1, usuarioId: 'jefe', fechaHora: '2026-10-09T14:00:00Z', motivoRechazoId: null, motivoRechazoTexto: null, notas: null },
        { id: '4', ciclo: 2, nivel: 2, resultado: 1, usuarioId: 'direccion', fechaHora: '2026-10-09T15:00:00Z', motivoRechazoId: null, motivoRechazoTexto: null, notas: 'Corregida' },
      ],
    } satisfies Pick<OrdenCompraDetalleResponse, 'cicloAutorizacion' | 'autorizaciones'>;
    render(<HistorialFirmasOc oc={oc} resolverNombre={(id) => id === 'jefe' ? 'Jefe de Compras' : 'Dirección'} />);
    expect(screen.getByRole('heading', { name: 'Ciclo 1' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Ciclo 2' })).toBeInTheDocument();
    expect(screen.getAllByText(/Nivel 1 · Jefe de Compras/)).toHaveLength(2);
    expect(screen.getAllByText(/Nivel 2 · Dirección/)).toHaveLength(2);
    const region = screen.getByRole('region', { name: 'Historial de firmas de autorización' });
    expect(within(region).getByText('Rechazado')).toBeInTheDocument();
    expect(within(region).getAllByText('Autorizado')).toHaveLength(3);
    expect(within(region).getByText('Motivo: Precio incorrecto')).toBeInTheDocument();
    expect(within(region).getByText('Corregir precio')).toBeInTheDocument();
    expect(within(region).getByText('Revisar cotización')).toBeInTheDocument();
    expect(region.querySelectorAll('time')).toHaveLength(4);
  });
  it('informa que un nuevo ciclo no tiene firmas', () => {
    render(<HistorialFirmasOc oc={{ cicloAutorizacion: 2, autorizaciones: [] }} resolverNombre={(id) => id} />);
    expect(screen.getByText('Sin firmas registradas.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /ciclo 2/ })).toBeInTheDocument();
  });
});
