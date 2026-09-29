import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ClienteDatosForm } from '@/modules/datos-maestros/components/ClienteDatosForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ClienteDetalle } from '@/modules/datos-maestros/api/types';

const base = {
  id: 'c-1', clave: 'CLI-001', referenciaExterna: 'AW-777',
  razonSocial: 'Vidrios del Centro SA', rfc: 'VCE010101AAA', regimenFiscal: '601',
  codigoPostalFiscal: '76100', usoCfdiDefault: null, formaPagoDefault: null,
  metodoPagoDefault: null, monedaDefault: 'MXN', esGenerico: false, origen: 1,
  email: null, telefono: null, datosFiscalesCompletos: true, estatus: 0, version: 1,
} as unknown as ClienteDetalle;

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated', accessToken: 't', expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 'a@b.com', nombre: 'A' }, empresas: [], currentEmpresaId: 'e',
    permisos, errorMessage: null,
  });
}

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/monedas', () => HttpResponse.json([])),
    http.get('*/api/v1/catalogos/regimenes-fiscales', () => HttpResponse.json([])),
    http.get('*/api/v1/catalogos/usos-cfdi', () => HttpResponse.json([])),
    http.get('*/api/v1/catalogos/formas-pago', () => HttpResponse.json([])),
  );
});
afterEach(() => {
  useAuthStore.setState({ status: 'idle', accessToken: null, permisos: [], user: null });
});

const G = PermisosCanonicos.DatosMaestrosClientesGestionar;

describe('<ClienteDatosForm> — fiscales en Origen=Aw', () => {
  it('sin fiscal-editar deshabilita razón social, RFC y CP con valor previo', () => {
    setPermisos([G]);
    render(<ClienteDatosForm cliente={base} />, { wrapper: createQueryWrapper() });
    expect(screen.getByDisplayValue('Vidrios del Centro SA')).toBeDisabled();
    expect(screen.getByDisplayValue('VCE010101AAA')).toBeDisabled();
    expect(screen.getByDisplayValue('76100')).toBeDisabled();
    expect(screen.getByText(/permiso de edición fiscal/i)).toBeInTheDocument();
  });

  it('con fiscal-editar quedan habilitados', () => {
    setPermisos([G, PermisosCanonicos.DatosMaestrosClientesFiscalEditar]);
    render(<ClienteDatosForm cliente={base} />, { wrapper: createQueryWrapper() });
    expect(screen.getByDisplayValue('VCE010101AAA')).toBeEnabled();
    expect(screen.getByDisplayValue('76100')).toBeEnabled();
  });

  it('un campo fiscal vacío en Aw sigue editable con gestionar', () => {
    setPermisos([G]);
    render(<ClienteDatosForm cliente={{ ...base, rfc: null }} />, { wrapper: createQueryWrapper() });
    expect(screen.getByDisplayValue('76100')).toBeDisabled();
    expect(screen.getAllByRole('textbox').some((e) => (e as HTMLInputElement).maxLength === 13 && !(e as HTMLInputElement).disabled)).toBe(true);
  });

  it('Manual no cambia: fiscales habilitados sin fiscal-editar', () => {
    setPermisos([G]);
    render(<ClienteDatosForm cliente={{ ...base, origen: 0 }} />, { wrapper: createQueryWrapper() });
    expect(screen.getByDisplayValue('VCE010101AAA')).toBeEnabled();
    expect(screen.getByDisplayValue('Vidrios del Centro SA')).toBeEnabled();
    expect(screen.queryByText(/permiso de edición fiscal/i)).not.toBeInTheDocument();
  });
});
