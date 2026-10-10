import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProveedorExpedienteSection } from '@/modules/datos-maestros/components/ProveedorExpedienteSection';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { EstadoAdjunto, EstadoExpediente } from '@/components/erp/adjuntos/api/types';

const BASE = '*/api/v1/datos-maestros/proveedores/p-1';

const adj = (id: string, tipo: string, nombre: string, estado: number, vigenteHasta: string | null) => ({
  id,
  tipoEntidad: 'proveedor',
  entidadId: 'p-1',
  tipoDocumentoId: tipo,
  tipoDocumentoCodigo: tipo,
  tipoDocumentoNombre: nombre,
  nombreArchivo: `${tipo}.pdf`,
  contentType: 'application/pdf',
  tamanoBytes: 1000,
  hashSha256: 'a'.repeat(64),
  vigenteHasta,
  estado,
  subidoPorId: 'u-1',
  subidoEn: '2026-10-01T10:00:00Z',
  bajaEn: null,
  bajaPorId: null,
  bajaMotivo: null,
});

const csf = adj('a-1', 'csf', 'Constancia de situación fiscal', EstadoAdjunto.Vencido, '2026-09-30');

const EXPEDIENTE = {
  tipoEntidad: 'proveedor',
  entidadId: 'p-1',
  completo: false,
  faltantes: ['contrato'],
  vencidos: ['csf'],
  porVencer: [],
  documentos: [
    {
      tipoDocumentoId: 'csf',
      codigo: 'csf',
      nombre: 'Constancia de situación fiscal',
      obligatorio: true,
      vigenciaMeses: 3,
      estado: EstadoExpediente.Vencido,
      actual: csf,
    },
    {
      tipoDocumentoId: 'contrato',
      codigo: 'contrato',
      nombre: 'Contrato',
      obligatorio: true,
      vigenciaMeses: null,
      estado: EstadoExpediente.Faltante,
      actual: null,
    },
  ],
};

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos,
    errorMessage: null,
  });
}

beforeEach(() => {
  mswServer.use(
    http.get(`${BASE}/expediente`, () => HttpResponse.json(EXPEDIENTE)),
    http.get(`${BASE}/adjuntos`, () => HttpResponse.json([csf])),
    http.get('*/api/v1/adjuntos/tipos', () => HttpResponse.json([])),
  );
});

afterEach(() => {
  vi.restoreAllMocks();
  useAuthStore.setState({ status: 'idle', accessToken: null, expiresAt: null, user: null, empresas: [], currentEmpresaId: null, permisos: [], errorMessage: null });
});

const pintar = () =>
  render(<ProveedorExpedienteSection proveedorId="p-1" />, { wrapper: createQueryWrapper() });

describe('<ProveedorExpedienteSection>', () => {
  it('sin adjuntos-ver no renderiza nada', () => {
    setPermisos([]);
    const { container } = pintar();
    expect(container).toBeEmptyDOMElement();
  });

  it('solo ver: muestra estados, faltantes y vencidos, sin acciones de subir ni baja', async () => {
    setPermisos([P.DatosMaestrosProveedoresAdjuntosVer]);
    pintar();
    await screen.findByText('Falta adjuntar: Contrato.');
    expect(screen.getByText(/Vencidos, hay que reemplazarlos: Constancia/)).toBeInTheDocument();
    const filas = screen.getAllByRole('listitem').filter((li) => li.hasAttribute('data-tipo'));
    expect(within(filas[0]).getByText('Vencido')).toBeInTheDocument();
    expect(within(filas[1]).getByText('Faltante')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /adjuntar|reemplazar/i })).not.toBeInTheDocument();
    await screen.findByRole('list', { name: /lista de adjuntos/i });
    expect(screen.queryByRole('button', { name: /dar de baja/i })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/dados de baja/i)).not.toBeInTheDocument();
  });

  it('expediente completo muestra el badge y oculta el aviso', async () => {
    mswServer.use(
      http.get(`${BASE}/expediente`, () =>
        HttpResponse.json({ ...EXPEDIENTE, completo: true, faltantes: [], vencidos: [] }),
      ),
    );
    setPermisos([P.DatosMaestrosProveedoresAdjuntosVer]);
    pintar();
    expect(await screen.findByText('Expediente completo')).toBeInTheDocument();
    expect(screen.queryByRole('note')).not.toBeInTheDocument();
  });

  it('con subir: Adjuntar/Reemplazar; vigencia solo en tipos con vigencia; sube con el tipo', async () => {
    setPermisos([P.DatosMaestrosProveedoresAdjuntosVer, P.DatosMaestrosProveedoresAdjuntosSubir]);
    let tipoEnviado: string | null = null;
    mswServer.use(
      http.post(`${BASE}/adjuntos`, async ({ request }) => {
        tipoEnviado = String((await request.formData()).get('tipoDocumentoId'));
        return HttpResponse.json(csf, { status: 201 });
      }),
    );
    const { container } = pintar();
    fireEvent.click(await screen.findByRole('button', { name: /adjuntar contrato/i }));
    expect(screen.queryByLabelText(/vigente hasta/i)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /reemplazar constancia/i }));
    expect(screen.getByLabelText(/vigente hasta/i)).toBeInTheDocument();

    const forms = container.querySelectorAll('[data-component="expediente-subida"]');
    const input = forms[0].querySelector('input[type=file]') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [new File(['%PDF'], 'c.pdf', { type: 'application/pdf' })] } });
    fireEvent.click(within(forms[0] as HTMLElement).getByRole('button', { name: /subir documento/i }));
    await waitFor(() => expect(tipoEnviado).toBe('csf'));
  });

  it('rechaza en cliente un formato no permitido y muestra el 422 del servidor', async () => {
    setPermisos([P.DatosMaestrosProveedoresAdjuntosVer, P.DatosMaestrosProveedoresAdjuntosSubir]);
    mswServer.use(
      http.post(`${BASE}/adjuntos`, () =>
        HttpResponse.json(
          { type: 'x', title: 't', status: 422, detail: 'La fecha de vigencia ya pasó.' },
          { status: 422 },
        ),
      ),
    );
    const { container } = pintar();
    fireEvent.click(await screen.findByRole('button', { name: /reemplazar constancia/i }));
    const input = container.querySelector('input[type=file]') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [new File(['x'], 'a.exe', { type: 'application/x-msdownload' })] } });
    expect(await screen.findByRole('alert')).toHaveTextContent(/formato no permitido/i);
    fireEvent.change(input, { target: { files: [new File(['%PDF'], 'c.pdf', { type: 'application/pdf' })] } });
    fireEvent.click(screen.getByRole('button', { name: /subir documento/i }));
    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent('La fecha de vigencia ya pasó.'),
    );
  });

  it('con baja: ofrece dar de baja y el toggle de bajas', async () => {
    setPermisos([P.DatosMaestrosProveedoresAdjuntosVer, P.DatosMaestrosProveedoresAdjuntosBaja]);
    pintar();
    expect(await screen.findByRole('button', { name: /dar de baja csf\.pdf/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/ver documentos dados de baja/i)).toBeInTheDocument();
  });

  it('403 al cargar el expediente cae al estado de error', async () => {
    mswServer.use(
      http.get(`${BASE}/expediente`, () =>
        HttpResponse.json({ type: 'x', title: 'Prohibido', status: 403 }, { status: 403 }),
      ),
    );
    setPermisos([P.DatosMaestrosProveedoresAdjuntosVer]);
    pintar();
    await waitFor(() => expect(screen.queryByText('Expediente documental')).not.toBeInTheDocument());
    expect(await screen.findByText(/prohibido|permiso|error/i)).toBeInTheDocument();
  });
});
