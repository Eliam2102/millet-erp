import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { TallerCierrePage } from '@/features/almacen/pages/TallerCierrePage';
import { useAuthStore } from '@/lib/auth/auth-store';

const TALLER = { id: 'suc-1', clave: 'CIRCUITO', nombre: 'Taller Circuito', tipo: 1, estatus: 0 };
const PLANTA = { id: 'suc-2', clave: 'CONKAL', nombre: 'Planta Conkal', tipo: 2, estatus: 0 };

const PRELLENADO = {
  sucursalId: 'suc-1',
  fecha: '2026-11-04',
  hojasDescargadas: 5,
  m2Descargados: 40.9152,
  pedidosSerie9000: 2,
  pedidosServidos: 4,
  enRevision: 0,
  descargas: [
    {
      numeroPedido: '90990010',
      posicion: 1,
      articuloRef: '100245',
      cantidad: 3,
      anchoMm: 2440,
      altoMm: 3660,
      m2: 26.7912,
      estado: 'RECIBIDO',
      folio: 'M-SAL2026-000012',
      error: null,
    },
  ],
  cierre: null,
};

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/sucursales', () =>
      HttpResponse.json({ items: [TALLER, PLANTA], offset: 0, limit: 500, total: 2 }),
    ),
    http.get('*/api/v1/almacen/taller/cierres/prellenado', () => HttpResponse.json(PRELLENADO)),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['almacen.salidas.leer-todas', 'almacen.salidas.capturar'],
    errorMessage: null,
  });
});

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<TallerCierrePage> — smoke', () => {
  it('prellena hojas y pedidos servidos y calcula el factor con su fórmula', async () => {
    render(<TallerCierrePage />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('90990010')).toBeInTheDocument());
    expect(screen.getByLabelText(/Hojas tomadas/i)).toHaveValue(5);
    expect(screen.getByLabelText(/Pedidos servidos/i)).toHaveValue(4);
    // La fórmula está a la vista y, sin m² cortados, no inventa un factor.
    expect(screen.getByText(/m² descargados por Serie 9000 ÷ m² cortados/i)).toBeInTheDocument();
    expect(screen.getByText(/sin m² cortados/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Cerrar día/i })).toBeDisabled();
  });

  it('un día cerrado no se edita y sin permiso de administrar no ofrece corregir', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/taller/cierres/prellenado', () =>
        HttpResponse.json({
          ...PRELLENADO,
          cierre: {
            id: 'c-1',
            sucursalId: 'suc-1',
            fecha: '2026-11-04',
            hojasTomadas: 5,
            retazosTomados: 2,
            m2Descargados: 40.9152,
            m2Cortados: 45,
            pedidosServidos: 4,
            pedidosSerie9000: 2,
            cortadores: 2,
            factorOptimizacion: 0.9092,
            formula: 'm² descargados por Serie 9000 ÷ m² cortados en el día',
            notas: null,
            cerradoAt: '2026-11-04T23:10:00Z',
            corregidoAt: null,
          },
        }),
      ),
    );
    render(<TallerCierrePage />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('Cerrado')).toBeInTheDocument());
    expect(screen.getByLabelText(/Hojas tomadas/i)).toBeDisabled();
    expect(screen.getByText('0.9092', { exact: false })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Corregir cierre/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Cerrar día/i })).not.toBeInTheDocument();
  });
});
