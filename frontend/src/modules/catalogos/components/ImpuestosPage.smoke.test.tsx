import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ImpuestosPage } from '@/modules/catalogos/components/ImpuestosPage';

const item = {
  id: 'tax-1', clave: 'QA01', nombre: 'Tasa ficticia', tipo: 'Traslado',
  factor: 'Tasa', tasa: 0.12, vigenteDesde: '2026-09-01',
  vigenteHasta: null, activo: true, fuente: 'Prueba local VILO', version: 1,
};

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated', accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'qa@test.local', nombre: 'QA' },
    empresas: [], currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.CompartidoCatalogosLeer,
      PermisosCanonicos.CompartidoCatalogosAdministrar],
    errorMessage: null,
  });
});

afterEach(() => {
  useAuthStore.setState({ status: 'idle', accessToken: null,
    expiresAt: null, user: null, empresas: [], currentEmpresaId: null,
    permisos: [], errorMessage: null });
});

describe('ImpuestosPage', () => {
  it('muestra vigencia y permite iniciar un alta sin presentar tasas como aprobadas', async () => {
    mswServer.use(http.get('*/api/v1/catalogos/impuestos', () => HttpResponse.json([item])));
    render(<ImpuestosPage />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('Tasa ficticia')).toBeInTheDocument());
    expect(screen.getByText(/requieren validación/i)).toBeInTheDocument();
    expect(screen.getByText(/2026-09-01/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /nueva referencia/i }));
    expect(screen.getByRole('form', { name: /nueva referencia/i })).toBeInTheDocument();
  });

  it('oculta mutaciones a un usuario de solo lectura', async () => {
    useAuthStore.setState({ permisos: [PermisosCanonicos.CompartidoCatalogosLeer] });
    mswServer.use(http.get('*/api/v1/catalogos/impuestos', () => HttpResponse.json([item])));
    render(<ImpuestosPage />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('Tasa ficticia')).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /nueva referencia/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /editar/i })).not.toBeInTheDocument();
  });

  it('soporta limpiar la fecha de consulta sin provocar error y cargando el catálogo', async () => {
    let capturedUrl = '';
    mswServer.use(
      http.get('*/api/v1/catalogos/impuestos', ({ request }) => {
        capturedUrl = request.url;
        return HttpResponse.json([item]);
      }),
    );
    render(<ImpuestosPage />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('Tasa ficticia')).toBeInTheDocument());

    const dateInput = screen.getByLabelText(/fecha de consulta/i);
    fireEvent.change(dateInput, { target: { value: '' } });

    await waitFor(() => {
      expect(capturedUrl).not.toContain('fecha=');
      expect(screen.getByText('Tasa ficticia')).toBeInTheDocument();
    });
  });
});
