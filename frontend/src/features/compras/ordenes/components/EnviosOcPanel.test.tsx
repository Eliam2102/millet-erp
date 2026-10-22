import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { EnviosOcPanel } from './EnviosOcPanel';
const mocks = vi.hoisted(() => ({
  enviar: vi.fn(),
  descargar: vi.fn(),
  permiso: true,
  data: {
    correoProveedor: null as string | null,
    esSimulado: true,
    puedeEnviar: true,
    motivoBloqueo: null as string | null,
    envios: [] as object[],
  },
}));
vi.mock('../api/useEnviosOc', () => ({
  useEnviosOc: () => ({ data: mocks.data, isPending: false, error: null }),
  useRegistrarEnvioOc: () => ({ mutateAsync: mocks.enviar, isPending: false }),
}));
vi.mock('../api/usePdfOrdenCompra', () => ({ descargarPdfOrdenCompra: mocks.descargar }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => mocks.permiso }));
describe('Envíos OC — datos DEMO ficticios', () => {
  beforeEach(() => {
    mocks.permiso = true;
    mocks.descargar.mockReset().mockResolvedValue(new Blob(['DEMO PDF ficticio']));
    URL.createObjectURL = vi.fn(() => 'blob:demo-ficticio');
    URL.revokeObjectURL = vi.fn();
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    mocks.data = {
      correoProveedor: null,
      esSimulado: true,
      puedeEnviar: true,
      motivoBloqueo: null,
      envios: [],
    };
    mocks.enviar.mockReset().mockResolvedValue({ resultado: 'simulado' });
  });
  it('G1.8-a envía al correo del proveedor y distingue la simulación', async () => {
    mocks.data.correoProveedor = 'proveedor-demo@example.test';
    render(<EnviosOcPanel ordenCompraId="oc-demo" />);
    fireEvent.click(screen.getByRole('button', { name: 'Enviar al proveedor' }));
    await waitFor(() =>
      expect(mocks.enviar).toHaveBeenCalledWith(
        expect.objectContaining({ destinatario: 'proveedor-demo@example.test', modo: 'correo' }),
      ),
    );
    expect(screen.getAllByText(/simulado/i).length).toBeGreaterThan(0);
  });
  it('G1.8-b sin correo pide captura antes del envío', () => {
    render(<EnviosOcPanel ordenCompraId="oc-demo" />);
    expect(screen.getByLabelText('Correo del proveedor *')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Enviar al proveedor' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Correo del proveedor *'), {
      target: { value: 'capturado-demo@example.test' },
    });
    expect(screen.getByRole('button', { name: 'Enviar al proveedor' })).toBeEnabled();
  });
  it('G1.8-c explica el bloqueo de una OC sin dos firmas', () => {
    mocks.data.puedeEnviar = false;
    mocks.data.motivoBloqueo = 'Autoriza la orden con dos firmas distintas antes de enviarla.';
    render(<EnviosOcPanel ordenCompraId="oc-demo" />);
    expect(screen.getByText(/dos firmas distintas/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Enviar al proveedor' })).toBeDisabled();
  });
  it('G1.8-d permite reenvío con clave nueva y muestra el historial', async () => {
    mocks.data.correoProveedor = 'proveedor-demo@example.test';
    mocks.data.envios = [1, 2].map((id) => ({
      id: String(id),
      destinatario: 'proveedor-demo@example.test',
      usuarioNombre: 'Usuario DEMO',
      fecha: '2026-10-22T18:00:00Z',
      modo: 'correo',
      resultado: 'simulado',
    }));
    render(<EnviosOcPanel ordenCompraId="oc-demo" />);
    expect(screen.getAllByText('Usuario DEMO')).toHaveLength(2);
    fireEvent.click(screen.getByRole('button', { name: 'Enviar al proveedor' }));
    await waitFor(() => expect(mocks.enviar).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByRole('button', { name: 'Enviar al proveedor' }));
    await waitFor(() => expect(mocks.enviar).toHaveBeenCalledTimes(2));
    expect(mocks.enviar.mock.calls[0][0].idempotencyKey).not.toBe(
      mocks.enviar.mock.calls[1][0].idempotencyKey,
    );
  });
  it('G1.8-e permite marcar manualmente después de descargar el PDF', async () => {
    mocks.data.correoProveedor = 'proveedor-demo@example.test';
    render(<EnviosOcPanel ordenCompraId="oc-demo" />);
    expect(screen.getByRole('button', { name: 'Marcar como enviada' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Descargar PDF para envío manual' }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Marcar como enviada' })).toBeEnabled(),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Marcar como enviada' }));
    await waitFor(() =>
      expect(mocks.enviar).toHaveBeenCalledWith(expect.objectContaining({ modo: 'manual' })),
    );
  });
  it('una descarga fallida no habilita el registro manual', async () => {
    mocks.data.correoProveedor = 'proveedor-demo@example.test';
    mocks.descargar.mockRejectedValueOnce(new Error('DEMO: fallo de descarga'));
    render(<EnviosOcPanel ordenCompraId="oc-demo" />);
    fireEvent.click(screen.getByRole('button', { name: 'Descargar PDF para envío manual' }));
    await screen.findByText(/No se pudo descargar el PDF/);
    expect(screen.getByRole('button', { name: 'Marcar como enviada' })).toBeDisabled();
    expect(mocks.enviar).not.toHaveBeenCalled();
  });
  it('sin permiso no permite registrar envíos', () => {
    mocks.permiso = false;
    render(<EnviosOcPanel ordenCompraId="oc-demo" />);
    expect(screen.queryByRole('button', { name: 'Enviar al proveedor' })).not.toBeInTheDocument();
  });
});
