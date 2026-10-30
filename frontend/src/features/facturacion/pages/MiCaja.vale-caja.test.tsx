import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { MiCaja } from '@/features/facturacion/pages/MiCaja';
import { useAuthStore } from '@/lib/auth/auth-store';

const descargarArchivo = vi.hoisted(() => vi.fn().mockResolvedValue(undefined));
vi.mock('@/features/facturacion/lib/descargas', () => ({ descargarArchivo }));

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
    // Sin handler, la consulta de sucursales cierra la sesión del store a media prueba.
    http.get('*/api/auth/sucursales', () => HttpResponse.json([])),
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

describe('<MiCaja> — Vale de Caja del retiro (86e3ww6tw)', () => {
  it('al registrar un retiro descarga su Vale de Caja con el folio asignado', async () => {
    descargarArchivo.mockClear();
    mswServer.use(
      http.post(`${BASE}/sesiones/:id/movimientos`, () =>
        HttpResponse.json({ id: 'm-9', sesionId: 'ses-1', importe: -3000, folioVale: 4 }),
      ),
    );
    render(<MiCaja />, { wrapper: createQueryWrapper() });

    fireEvent.click(await screen.findByRole('button', { name: /Registrar movimiento/i }));
    const [tipo] = screen.getAllByLabelText('select');
    fireEvent.change(tipo!, { target: { value: '3' } });
    fireEvent.change(screen.getByLabelText(/Importe/), { target: { value: '3000' } });
    fireEvent.change(screen.getByLabelText(/Motivo del retiro/), {
      target: { value: 'Pago de flete ficticio de prueba' },
    });
    // Guardar se habilita cuando llega el catálogo de formas de pago.
    await vi.waitFor(() => expect(screen.getByRole('button', { name: 'Guardar' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await vi.waitFor(() =>
      expect(descargarArchivo).toHaveBeenCalledWith(
        '/api/v1/facturacion/cajas/movimientos/m-9/vale.pdf',
        'vale-de-caja-4.pdf',
      ),
    );
  });

  it('un depósito no descarga vale', async () => {
    descargarArchivo.mockClear();
    let posts = 0;
    mswServer.use(
      http.post(`${BASE}/sesiones/:id/movimientos`, () => {
        posts += 1;
        return HttpResponse.json({ id: 'm-10', sesionId: 'ses-1', importe: 100, folioVale: null });
      }),
    );
    render(<MiCaja />, { wrapper: createQueryWrapper() });

    fireEvent.click(await screen.findByRole('button', { name: /Registrar movimiento/i }));
    fireEvent.change(screen.getByLabelText(/Importe/), { target: { value: '100' } });
    fireEvent.change(screen.getByLabelText(/Descripción/), { target: { value: 'Depósito ficticio' } });
    // Guardar se habilita cuando llega el catálogo de formas de pago.
    await vi.waitFor(() => expect(screen.getByRole('button', { name: 'Guardar' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await vi.waitFor(() => expect(posts).toBe(1));
    await new Promise((r) => setTimeout(r, 50));
    expect(descargarArchivo).not.toHaveBeenCalled();
  });

  it('reimprime el vale desde el renglón del retiro con el mismo folio', async () => {
    descargarArchivo.mockClear();
    const retiro = {
      id: 'm-7',
      tipo: 'Retiro',
      formaPago: '02',
      importe: -500,
      moneda: 'MXN',
      descripcion: 'Pago de flete ficticio de prueba',
      referencia: null,
      cobroMostradorId: null,
      usuarioId: 'u-test',
      createdAt: '2026-07-11T15:00:00Z',
      folioVale: 2,
    };
    const deposito = { ...retiro, id: 'm-8', tipo: 'Deposito', importe: 100, folioVale: null };
    mswServer.use(
      http.get(`${BASE}/sesion-actual`, () =>
        HttpResponse.json({
          sesion: { ...sesionAbierta, movimientos: [retiro, deposito] },
          diaAnteriorPendiente: false,
        }),
      ),
    );
    render(<MiCaja />, { wrapper: createQueryWrapper() });

    fireEvent.click(await screen.findByRole('button', { name: 'Vale de caja folio 2' }));

    await vi.waitFor(() =>
      expect(descargarArchivo).toHaveBeenCalledWith(
        '/api/v1/facturacion/cajas/movimientos/m-7/vale.pdf',
        'vale-de-caja-2.pdf',
      ),
    );
    expect(screen.getAllByRole('button', { name: /Vale de caja folio/ })).toHaveLength(1);
  });
});
