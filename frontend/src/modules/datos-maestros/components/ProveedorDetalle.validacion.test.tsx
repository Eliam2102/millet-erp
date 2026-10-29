import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProveedorDetalle } from '@/modules/datos-maestros/components/ProveedorDetalle';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EstatusCatalogo } from '@/modules/datos-maestros/api/types';

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    className,
    ...rest
  }: {
    children: React.ReactNode;
    to: string;
    className?: string;
    [key: string]: unknown;
  }) => (
    <a href={to} className={className} {...rest}>
      {children}
    </a>
  ),
  useParams: () => ({ id: 'p-rev' }),
}));

const PROVEEDOR_EN_REVISION = {
  id: 'p-rev',
  clave: 'PROV-REV-01',
  claveLegacy: null,
  razonSocial: 'Vidrios del Norte S.A. de C.V.',
  nombreComercial: 'Vidrios del Norte',
  rfc: 'VNO010101XYZ',
  tipoPersona: 0,
  condicionesPagoDias: 30,
  monedaPreferidaId: null,
  email: 'contacto@vidriosnorte.mx',
  telefono: '+52 55 1234 5678',
  estatus: EstatusCatalogo.EnRevision,
  validadoEn: null,
  motivoRechazo: null,
  origen: 0,
  regimenFiscalCodigo: '601',
  conceptoRetencion: null,
};

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'cxp@millet.mx', nombre: 'Analista CxP' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [
      PermisosCanonicos.DatosMaestrosProveedoresGestionar,
      PermisosCanonicos.DatosMaestrosProveedoresValidar,
      PermisosCanonicos.DatosMaestrosProveedoresAdjuntosVer,
    ],
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

describe('<ProveedorDetalle> — Validación y Rechazo de CxP (G1.1)', () => {
  it('muestra badge En revisión, banner y botones si usuario tiene permiso validar pero expediente incompleto desactiva Validar', async () => {
    mswServer.use(
      http.get('*/api/v1/datos-maestros/proveedores/p-rev', () =>
        HttpResponse.json(PROVEEDOR_EN_REVISION),
      ),
      http.get('*/api/v1/datos-maestros/proveedores/p-rev/expediente', () =>
        HttpResponse.json({
          tipoEntidad: 'proveedor',
          entidadId: 'p-rev',
          completo: false,
          documentos: [],
          faltantes: ['CSF', 'CARATULA_BANCARIA'],
          vencidos: [],
          porVencer: [],
        }),
      ),
    );

    render(<ProveedorDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() => {
      expect(screen.getByText('PROV-REV-01')).toBeInTheDocument();
    });

    // Badge "En revisión" visible
    expect(screen.getByText('En revisión')).toBeInTheDocument();

    // Banner informativo visible
    expect(
      screen.getByText(/Proveedor en revisión por Cuentas por Pagar/i),
    ).toBeInTheDocument();

    // Botones de CxP presentes
    const btnValidar = screen.getByRole('button', {
      name: /validar proveedor/i,
    });
    const btnRechazar = screen.getByRole('button', { name: /rechazar/i });

    expect(btnValidar).toBeInTheDocument();
    expect(btnRechazar).toBeInTheDocument();

    // Validar está deshabilitado porque el expediente está incompleto
    expect(btnValidar).toBeDisabled();
  });

  it('el tooltip de Validar suma faltantes y vencidos', async () => {
    mswServer.use(
      http.get('*/api/v1/datos-maestros/proveedores/p-rev', () => HttpResponse.json(PROVEEDOR_EN_REVISION)),
      http.get('*/api/v1/datos-maestros/proveedores/p-rev/expediente', () =>
        HttpResponse.json({
          tipoEntidad: 'proveedor', entidadId: 'p-rev', completo: false, documentos: [],
          faltantes: ['contrato'], vencidos: ['comprobante_domicilio', 'constancia_situacion_fiscal'], porVencer: [],
        }),
      ),
    );
    render(<ProveedorDetalle />, { wrapper: createQueryWrapper() });
    const btn = await screen.findByRole('button', { name: /validar proveedor/i });
    await waitFor(() =>
      expect(btn).toHaveAttribute('title', 'Expediente incompleto (1 faltante y 2 vencidos)'),
    );
    expect(btn).toBeDisabled();
  });

  it('no permite validar un proveedor sin clasificar y explica por qué', async () => {
    mswServer.use(
      http.get('*/api/v1/datos-maestros/proveedores/p-rev', () =>
        HttpResponse.json({ ...PROVEEDOR_EN_REVISION, origen: null, regimenFiscalCodigo: null }),
      ),
      http.get('*/api/v1/datos-maestros/proveedores/p-rev/expediente', () =>
        HttpResponse.json({
          tipoEntidad: 'proveedor', entidadId: 'p-rev', completo: true, documentos: [],
          faltantes: [], vencidos: [], porVencer: [],
        }),
      ),
    );
    render(<ProveedorDetalle />, { wrapper: createQueryWrapper() });
    const btn = await screen.findByRole('button', { name: /validar proveedor/i });
    expect(btn).toBeDisabled();
    expect(btn).toHaveAttribute('title', expect.stringMatching(/clasifica al proveedor/i));
  });

  it('habilita botón Validar proveedor cuando el expediente está completo', async () => {
    mswServer.use(
      http.get('*/api/v1/datos-maestros/proveedores/p-rev', () =>
        HttpResponse.json(PROVEEDOR_EN_REVISION),
      ),
      http.get('*/api/v1/datos-maestros/proveedores/p-rev/expediente', () =>
        HttpResponse.json({
          tipoEntidad: 'proveedor',
          entidadId: 'p-rev',
          completo: true,
          documentos: [],
          faltantes: [],
          vencidos: [],
          porVencer: [],
        }),
      ),
    );

    render(<ProveedorDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() => {
      expect(screen.getByText('PROV-REV-01')).toBeInTheDocument();
    });

    const btnValidar = screen.getByRole('button', {
      name: /validar proveedor/i,
    });
    expect(btnValidar).toBeEnabled();
  });

  it('oculta botones Validar y Rechazar a usuarios sin permiso datos_maestros.proveedores.validar', async () => {
    // Usuario sin permiso de validar (ej. Compras)
    useAuthStore.setState({
      permisos: [PermisosCanonicos.DatosMaestrosProveedoresGestionar],
    });

    mswServer.use(
      http.get('*/api/v1/datos-maestros/proveedores/p-rev', () =>
        HttpResponse.json(PROVEEDOR_EN_REVISION),
      ),
      http.get('*/api/v1/datos-maestros/proveedores/p-rev/expediente', () =>
        HttpResponse.json({
          tipoEntidad: 'proveedor',
          entidadId: 'p-rev',
          completo: true,
          documentos: [],
          faltantes: [],
          vencidos: [],
          porVencer: [],
        }),
      ),
    );

    render(<ProveedorDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() => {
      expect(screen.getByText('PROV-REV-01')).toBeInTheDocument();
    });

    expect(
      screen.queryByRole('button', { name: /validar proveedor/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /^rechazar$/i }),
    ).not.toBeInTheDocument();
  });

  it('abre diálogo de rechazo y valida longitud mínima de 5 caracteres', async () => {
    mswServer.use(
      http.get('*/api/v1/datos-maestros/proveedores/p-rev', () =>
        HttpResponse.json(PROVEEDOR_EN_REVISION),
      ),
      http.get('*/api/v1/datos-maestros/proveedores/p-rev/expediente', () =>
        HttpResponse.json({
          tipoEntidad: 'proveedor',
          entidadId: 'p-rev',
          completo: false,
          documentos: [],
          faltantes: [],
          vencidos: [],
          porVencer: [],
        }),
      ),
    );

    render(<ProveedorDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() => {
      expect(screen.getByText('PROV-REV-01')).toBeInTheDocument();
    });

    const btnRechazar = screen.getByRole('button', { name: /rechazar/i });
    fireEvent.click(btnRechazar);

    // Diálogo abierto
    expect(
      screen.getByRole('dialog', { name: /rechazar proveedor/i }),
    ).toBeInTheDocument();

    const btnConfirmarRechazo = screen.getByRole('button', {
      name: /confirmar rechazo/i,
    });
    // Deshabilitado inicialmente (motivo vacío)
    expect(btnConfirmarRechazo).toBeDisabled();

    const textarea = screen.getByPlaceholderText(/ejemplo: la constancia/i);
    // Motivo con menos de 5 caracteres
    fireEvent.change(textarea, { target: { value: 'bad' } });
    expect(btnConfirmarRechazo).toBeDisabled();
    expect(screen.getByText(/Mínimo 5 caracteres/i)).toBeInTheDocument();

    // Motivo válido >= 5 caracteres
    fireEvent.change(textarea, {
      target: { value: 'Documentación incompleta y CSF vencida' },
    });
    expect(btnConfirmarRechazo).toBeEnabled();
  });
});

it.each(['CargaSap', null])('avisa expediente incompleto del activo y distingue origen %s', async (origenCarga) => {
  mswServer.use(
    http.get('*/api/v1/datos-maestros/proveedores/p-rev', () => HttpResponse.json({
      ...PROVEEDOR_EN_REVISION, estatus: EstatusCatalogo.Activo, origenCarga,
    })),
    http.get('*/api/v1/datos-maestros/proveedores/p-rev/expediente', () => HttpResponse.json({
      completo: false, documentos: [{ codigo: 'contrato', nombre: 'Contrato' }],
      faltantes: ['contrato'], vencidos: [], porVencer: [],
    })),
  );
  // El aviso usa la consulta; el editor documental se prueba en su propia suite.
  useAuthStore.setState({ permisos: [PermisosCanonicos.DatosMaestrosProveedoresValidar] });
  render(<ProveedorDetalle />, { wrapper: createQueryWrapper() });
  const aviso = await screen.findByRole('alert');
  expect(aviso).toHaveTextContent('Expediente incompleto: faltan 1 documentos (Contrato).');
  expect(aviso).toHaveClass('bg-warning-note-bg');
  expect(screen.getByRole('link', { name: 'Ver expediente' })).toHaveAttribute('href', '#expediente-proveedor');
  expect(aviso.textContent?.includes('Cargado desde SAP sin documentos.')).toBe(origenCarga === 'CargaSap');
});

it('muestra el aviso desde el detalle sin permiso de adjuntos y lo oculta al completar', async () => {
  useAuthStore.setState({ permisos: [PermisosCanonicos.DatosMaestrosProveedoresGestionar] });
  let pendientes = ['Contrato'];
  mswServer.use(http.get('*/api/v1/datos-maestros/proveedores/p-rev', () => HttpResponse.json({
    ...PROVEEDOR_EN_REVISION, estatus: EstatusCatalogo.Activo, origenCarga: 'CargaSap', documentosPendientes: pendientes,
  })));
  const vista = render(<ProveedorDetalle />, { wrapper: createQueryWrapper() });
  expect(await screen.findByRole('alert')).toHaveTextContent('Contrato');
  vista.unmount();
  pendientes = [];
  render(<ProveedorDetalle />, { wrapper: createQueryWrapper() });
  await screen.findByText('PROV-REV-01');
  expect(screen.queryByText(/Expediente incompleto:/)).not.toBeInTheDocument();
});
