import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProveedorDatosForm } from '@/modules/datos-maestros/components/ProveedorDatosForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ProveedorDetalle } from '@/modules/datos-maestros/api/types';

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
  claveAw: 123,
  email: null,
  telefono: null,
  estatus: 0,
  toleranciaFacturaContraOcMxn: null,
  validadoEn: null,
  motivoRechazo: null,
  origen: null,
  regimenFiscalCodigo: null,
  conceptoRetencion: null,
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

describe('<ProveedorDatosForm> — Clave de A+W', () => {
  it('campo vacío envía limpiarClaveAw: true', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.patch('*/api/v1/catalogos/proveedores/p-1', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<ProveedorDatosForm proveedor={PROVEEDOR} />, { wrapper: createQueryWrapper() });
    fireEvent.change(screen.getByLabelText(/clave de a\+w/i), { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));
    await waitFor(() => expect(body).not.toBeNull());
    expect(body!.limpiarClaveAw).toBe(true);
    expect(body!.claveAw ?? null).toBeNull();
  });

  it('valor nuevo envía claveAw y 409 se muestra en el campo', async () => {
    let body: Record<string, unknown> | null = null;
    const detail = 'Ya existe el proveedor P-002 · Otro SA con clave de A+W 456.';
    mswServer.use(
      http.patch('*/api/v1/catalogos/proveedores/p-1', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          { type: 'about:blank', title: 'Conflicto', status: 409, code: 'PROVEEDOR_CLAVE_AW_DUPLICADA', detail },
          { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
        );
      }),
    );
    render(<ProveedorDatosForm proveedor={PROVEEDOR} />, { wrapper: createQueryWrapper() });
    fireEvent.change(screen.getByLabelText(/clave de a\+w/i), { target: { value: '456' } });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));
    await waitFor(() => expect(screen.getByText(detail)).toBeInTheDocument());
    expect(body!.claveAw).toBe(456);
    expect(body!.limpiarClaveAw).toBe(false);
  });
});
