import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProveedorDatosForm } from '@/modules/datos-maestros/components/ProveedorDatosForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ProveedorDetalle } from '@/modules/datos-maestros/api/types';

/**
 * F1-ADM-05 — el 409 `PROVEEDOR_RFC_DUPLICADO` (Paso 1 backend) se
 * muestra en el campo RFC con el mensaje legible del backend, mismo
 * mecanismo que `PROVEEDOR_CLAVE_DUPLICADA` en el alta.
 */

const PROVEEDOR: ProveedorDetalle = {
  id: 'p-1',
  clave: 'P-001',
  claveLegacy: null,
  razonSocial: 'Proveedor Uno SA',
  nombreComercial: null,
  rfc: 'PUN010101AAA',
  tipoPersona: 0,
  condicionesPagoDias: null,
  monedaPreferidaId: null,
  email: null,
  telefono: null,
  estatus: 0,
  validadoEn: null,
  motivoRechazo: null,
};

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.CompartidoCatalogosAdministrar],
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

describe('<ProveedorDatosForm> — 409 PROVEEDOR_RFC_DUPLICADO', () => {
  it('muestra el mensaje del backend en el campo RFC', async () => {
    mswServer.use(
      http.patch('*/api/v1/catalogos/proveedores/p-1', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Conflicto',
            status: 409,
            code: 'PROVEEDOR_RFC_DUPLICADO',
            detail:
              'Ya existe el proveedor P-002 · Otro Proveedor SA con RFC OTR010101AAA.',
          },
          {
            status: 409,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    render(<ProveedorDatosForm proveedor={PROVEEDOR} />, {
      wrapper: createQueryWrapper(),
    });

    const rfcInput = screen.getByDisplayValue('PUN010101AAA');
    fireEvent.change(rfcInput, { target: { value: 'OTR010101AAA' } });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() =>
      expect(
        screen.getByText(
          'Ya existe el proveedor P-002 · Otro Proveedor SA con RFC OTR010101AAA.',
        ),
      ).toBeInTheDocument(),
    );
  });
});
