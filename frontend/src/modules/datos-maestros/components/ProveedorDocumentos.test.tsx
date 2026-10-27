import { afterEach, beforeEach, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProveedorDatosForm } from './ProveedorDatosForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { OrigenProveedor, type ProveedorDetalle } from '@/modules/datos-maestros/api/types';
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

it.each([OrigenProveedor.Nacional, OrigenProveedor.Extranjero])(
  'Tax ID solo visible para extranjero: %s',
  (origen) => {
    render(<ProveedorDatosForm proveedor={{ ...PROVEEDOR, origen }} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByLabelText('Calle')).toBeInTheDocument();
    const campo = screen.queryByLabelText('Tax ID (identificación fiscal extranjera)');
    if (origen === OrigenProveedor.Extranjero) expect(campo).toBeInTheDocument();
    else expect(campo).not.toBeInTheDocument();
  },
);
