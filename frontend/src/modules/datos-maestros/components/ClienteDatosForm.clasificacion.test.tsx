import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ClienteDatosForm } from '@/modules/datos-maestros/components/ClienteDatosForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ClienteDetalle } from '@/modules/datos-maestros/api/types';

/**
 * H5/D7 — un cliente sin clasificación no puede guardarse: la validación
 * de cliente lo bloquea en el front y no se llama al PATCH.
 */
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
}));

const CLIENTE: ClienteDetalle = {
  clasificacionId: null,
  id: 'c-1',
  clave: 'CLI-001',
  referenciaExterna: null,
  razonSocial: 'Cliente Uno SA',
  rfc: 'CUN010101AAA',
  regimenFiscal: '601',
  codigoPostalFiscal: '76100',
  usoCfdiDefault: null,
  formaPagoDefault: null,
  metodoPagoDefault: null,
  monedaDefault: 'MXN',
  esGenerico: false,
  origen: 0,
  email: null,
  telefono: null,
  numRegIdTrib: null,
  paisResidencia: null,
  domicilioExtranjeroCalle: null,
  domicilioExtranjeroEstado: null,
  domicilioExtranjeroCodigoPostal: null,
  datosFiscalesCompletos: true,
  estatus: 0,
  version: 1,
};

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/monedas', () => HttpResponse.json([])),
    http.get('*/api/v1/catalogos/regimenes-fiscales', () =>
      HttpResponse.json([]),
    ),
    http.get('*/api/v1/catalogos/usos-cfdi', () => HttpResponse.json([])),
    http.get('*/api/v1/catalogos/formas-pago', () => HttpResponse.json([])),
    http.get('*/api/v1/datos-maestros/clasificaciones-cliente', () =>
      HttpResponse.json([
        {
          id: '00000012-0001-0000-0000-000000000001',
          nombre: 'Constructora',
          descripcion: '',
          activa: true,
          version: 1,
        },
      ]),
    ),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.DatosMaestrosClientesGestionar],
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

describe('<ClienteDatosForm> — clasificación obligatoria al guardar', () => {
  it('sin clasificación no llama al PATCH y muestra "Elige una clasificación"', async () => {
    let patches = 0;
    mswServer.use(
      http.patch('*/api/v1/datos-maestros/clientes/:id', () => {
        patches += 1;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    render(<ClienteDatosForm cliente={CLIENTE} />, {
      wrapper: createQueryWrapper(),
    });

    // Cambio de razón social: deja el form sucio para habilitar "Guardar".
    fireEvent.change(screen.getByDisplayValue('Cliente Uno SA'), {
      target: { value: 'Cliente Uno SA de CV' },
    });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Elige una clasificación',
      ),
    );
    expect(patches).toBe(0);
  });
});
