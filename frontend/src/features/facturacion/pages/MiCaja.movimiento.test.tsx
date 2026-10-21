import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { MiCaja } from '@/features/facturacion/pages/MiCaja';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

// Select de Radix → <select> nativo: el test solo necesita elegir el tipo.
vi.mock('@/components/ui/select', () => ({
  Select: ({
    value,
    onValueChange,
    children,
  }: {
    value: string;
    onValueChange: (v: string) => void;
    children: React.ReactNode;
  }) => (
    <select aria-label="select" value={value} onChange={(e) => onValueChange(e.target.value)}>
      {children}
    </select>
  ),
  SelectTrigger: () => null,
  SelectValue: () => null,
  SelectContent: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  SelectItem: ({ value, children }: { value: string; children: React.ReactNode }) => (
    <option value={value}>{children}</option>
  ),
}));

const BASE = '*/api/v1/facturacion/cajas';

const sesionAbierta = {
  id: 'ses-1',
  cajaId: 'c-1',
  cajaNombre: 'Caja Mostrador Conkal',
  sucursalId: 's-1',
  responsableUsuarioId: 'u-test',
  estado: 'Abierta',
  diaOperacion: '2026-07-11',
  fechaApertura: '2026-07-11T14:00:00Z',
  fechaCierre: null,
  fondoApertura: 500,
  efectivoTeorico: null,
  efectivoDeclarado: null,
  diferencia: null,
  cierreExtemporaneo: false,
  notasCierre: null,
  autorizacionAperturaId: null,
  version: 1,
  cortes: [],
  movimientos: [],
  totalesPorForma: [{ formaPago: '01', montoSistema: 500, montoDeclarado: null }],
};

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['facturacion.caja.operar'],
    errorMessage: null,
  });
  mswServer.use(
    http.get('*/api/v1/facturacion/cobros', () => HttpResponse.json([])),
    http.get(`${BASE}/sesion-actual`, () =>
      HttpResponse.json({ sesion: sesionAbierta, diaAnteriorPendiente: false }),
    ),
    http.get('*/api/v1/catalogos/formas-pago', () =>
      HttpResponse.json([{ claveSat: '01', descripcion: 'Efectivo', activa: true }]),
    ),
  );
});

afterEach(() => {
  useAuthStore.setState({ status: 'idle', accessToken: null, permisos: [], user: null });
});

describe('<MiCaja> — retiro con motivo obligatorio (86e3tfdwa)', () => {
  it('no envía un retiro con motivo de menos de 15 caracteres y muestra el contador', async () => {
    let posts = 0;
    mswServer.use(
      http.post(`${BASE}/sesiones/:id/movimientos`, () => {
        posts += 1;
        return HttpResponse.json({ id: 'm-2', sesionId: 'ses-1', importe: -100 });
      }),
    );
    render(<MiCaja />, { wrapper: createQueryWrapper() });

    fireEvent.click(await screen.findByRole('button', { name: /Registrar movimiento/i }));
    const [tipo] = screen.getAllByLabelText('select');
    fireEvent.change(tipo!, { target: { value: '3' } });
    fireEvent.change(screen.getByLabelText(/Importe/), { target: { value: '100' } });
    fireEvent.change(screen.getByLabelText(/Motivo del retiro/), {
      target: { value: 'Camión taller' },
    });

    expect(screen.getByText(/mínimo 15 caracteres \(13\/15\)/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
    await new Promise((r) => setTimeout(r, 50));
    expect(posts).toBe(0);

    fireEvent.change(screen.getByLabelText(/Motivo del retiro/), {
      target: { value: 'Reparación del camión de reparto' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
    await vi.waitFor(() => expect(posts).toBe(1));
  });
});
