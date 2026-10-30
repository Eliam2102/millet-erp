import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaConciliaciones } from './BandejaConciliaciones';

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
  Link: ({ children }: { children: React.ReactNode }) => <a href="#">{children}</a>,
}));

vi.mock('@/lib/auth/useHasPermission', () => ({
  useHasPermission: () => true,
}));

const mockCuenta = {
  id: '00000000-0000-0000-0000-000000000001',
  empresaId: '00000000-0000-0000-0000-000000000002',
  banco: 'BBVA',
  numeroCuenta: '0123456789',
  clabe: '012180001234567890',
  moneda: 'MXN',
  activa: true,
  saldoActual: 50000,
};

const mockConciliacion = {
  id: '00000000-0000-0000-0000-000000000010',
  cuentaBancariaId: '00000000-0000-0000-0000-000000000001',
  banco: 'BBVA',
  numeroCuenta: '0123456789',
  moneda: 'MXN',
  periodoAnio: 2026,
  periodoMes: 10,
  saldoExtracto: 25000,
  saldoLibros: 25000,
  diferencia: 0,
  estado: 1, // Abierta
  cerradaPor: null,
  cerradaEn: null,
  motivoReapertura: null,
  firmanteNombre: null,
  firmantePuesto: null,
  actaGeneradaEn: null,
  lineas: [
    {
      id: '00000000-0000-0000-0000-000000000020',
      numeroLinea: 1,
      fecha: '2026-10-05T00:00:00Z',
      descripcion: 'DEPOSITO CLIENTE ACME',
      referencia: 'DEP123',
      cargo: null,
      abono: 25000,
      monto: 25000,
      sentido: 1,
      estado: 2, // Emparejada
      matchTipo: 1,
      matchMovimientoId: '00000000-0000-0000-0000-000000000030',
      matches: [],
      candidatosRevisionIds: [],
      partidaTransito: false,
      partidaTransitoTipo: null,
      partidaTransitoResponsable: null,
    },
  ],
  bitacora: [],
  movimientosPeriodo: [
    {
      id: '00000000-0000-0000-0000-000000000030',
      fechaValor: '2026-10-05T00:00:00Z',
      sentido: 1,
      monto: 25000,
      moneda: 'MXN',
      referenciaBancaria: 'DEP123',
      conceptoId: null,
      conceptoNombre: 'Cobranza cliente',
      estadoConciliacion: 2,
    },
  ],
};

describe('<BandejaConciliaciones>', () => {
  it('renderiza cabecera, controles de período y estado cuadrado de la conciliación', async () => {
    mswServer.use(
      http.get('*/api/v1/tesoreria/cuentas', () => HttpResponse.json([mockCuenta])),
      http.get('*/api/v1/tesoreria/conceptos', () => HttpResponse.json([])),
      http.get('*/api/v1/tesoreria/conciliaciones', () => HttpResponse.json([mockConciliacion])),
      http.get('*/api/v1/tesoreria/conciliaciones/00000000-0000-0000-0000-000000000010', () =>
        HttpResponse.json(mockConciliacion)
      )
    );

    render(<BandejaConciliaciones />, { wrapper: createQueryWrapper() });

    expect(await screen.findByRole('heading', { name: 'Conciliación bancaria' })).toBeInTheDocument();
    expect(await screen.findByText('Saldo Extracto Banco')).toBeInTheDocument();
    expect(await screen.findByText('Saldo Libros (ERP)')).toBeInTheDocument();
    expect(await screen.findByText('✓ Conciliación cuadrada ($0.00)')).toBeInTheDocument();
    expect(await screen.findByText('Cerrar con Acta')).toBeInTheDocument();
    expect(await screen.findByText('DEPOSITO CLIENTE ACME')).toBeInTheDocument();
    expect(await screen.findByText('Cobranza cliente')).toBeInTheDocument();
  });
});
