import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { hoyLocalISO } from '@/lib/datetime';
import { CotizacionSheet } from '@/features/compras/components/CotizacionSheet';
import type { CotizacionComparativo, PartidaComparativo } from '@/features/compras/api/types';

const mocks = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  editar: vi.fn(),
  upload: vi.fn(),
}));

vi.mock('@/features/compras/api/useCotizaciones', () => ({
  useRegistrarCotizacion: () => ({ mutateAsync: mocks.mutateAsync, isPending: false }),
  useEditarCotizacion: () => ({ mutateAsync: mocks.editar, isPending: false }),
}));
vi.mock('@/lib/hooks/useUploadFile', () => ({
  useUploadFile: () => ({ upload: mocks.upload, isUploading: false }),
}));
vi.mock('@/components/erp', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/erp')>()),
  ProveedorSelector: ({ onChange }: { onChange: (v: string) => void }) => (
    <button type="button" onClick={() => onChange('00000005-0001-0000-0000-000000000001')}>
      Elegir proveedor de prueba
    </button>
  ),
  DatePickerField: ({ onChange, id }: { onChange: (v: string) => void; id?: string }) => (
    <input id={id} type="date" onChange={(e) => onChange(e.target.value)} />
  ),
}));

const partida: PartidaComparativo = {
  lineaRequisicionId: '00000007-0001-0000-0000-000000000001',
  posicion: 1,
  articuloId: 'art-1',
  articuloClave: 'SIL-01',
  articuloNombre: 'Silicón',
  cantidadDeCompra: 4,
  saldoPorComprar: 4,
  unidad: 'HOJA',
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.mutateAsync.mockResolvedValue({ id: 'cot-nueva', version: 1 });
  mocks.upload.mockResolvedValue({});
});

describe('CotizacionSheet', () => {
  it('envía el payload sin unidadRequisicion y sube el PDF con el tipo de documento', async () => {
    const onClose = vi.fn();
    render(
      <CotizacionSheet abierto onClose={onClose} requisicionId="rq-1" folio="MID2026-010009" partidas={[partida]} />,
      { wrapper: createQueryWrapper() },
    );

    fireEvent.click(screen.getByRole('button', { name: 'Elegir proveedor de prueba' }));
    fireEvent.change(screen.getByLabelText(/Vigente hasta/), { target: { value: hoyLocalISO() } });
    fireEvent.change(screen.getByLabelText(/Unidad cotizada/), { target: { value: 'M2' } });
    fireEvent.change(await screen.findByLabelText(/Equivale a/), { target: { value: '0.4' } });
    fireEvent.change(screen.getByLabelText(/^Precio/), { target: { value: '10' } });
    const pdf = new File(['%PDF-1.4'], 'cot.pdf', { type: 'application/pdf' });
    fireEvent.change(screen.getByLabelText(/Cotización del proveedor \(PDF\)/), { target: { files: [pdf] } });

    fireEvent.click(await screen.findByRole('button', { name: 'Registrar cotización' }));

    await waitFor(() => expect(mocks.mutateAsync).toHaveBeenCalledTimes(1));
    const { values } = mocks.mutateAsync.mock.calls[0]![0];
    expect(values.lineas[0].factorAUnidadRq).toBe(0.4);
    expect(values.lineas[0].unidadCotizada).toBe('M2');
    expect(values.lineas[0]).not.toHaveProperty('unidadRequisicion');
    await waitFor(() => expect(mocks.upload).toHaveBeenCalledTimes(1));
    const subida = mocks.upload.mock.calls[0]![0];
    expect(subida.url).toBe('/api/v1/compras/cotizaciones/cot-nueva/adjuntos');
    expect(subida.extraFields.tipoDocumentoId).toBe('00000011-0006-0000-0000-000000000004');
    await waitFor(() => expect(onClose).toHaveBeenCalledWith({ force: true }));
  });

  it('deshabilita el botón principal y explica qué falta', () => {
    render(<CotizacionSheet abierto onClose={vi.fn()} requisicionId="rq-1" folio="MID2026-010009" partidas={[partida]} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByRole('button', { name: 'Registrar cotización' })).toBeDisabled();
    expect(screen.getByText(/Sin PDF la cotización queda registrada/)).toBeInTheDocument();
  });

  it('en modo edición precarga los valores y guarda con PUT', async () => {
    mocks.editar.mockResolvedValue({ id: 'cot-1', version: 2 });
    const onClose = vi.fn();
    const cotizacion = {
      id: 'cot-1', proveedorId: '00000005-0001-0000-0000-000000000001', folioProveedor: 'COT-9', moneda: 'USD',
      fechaCotizacion: '2026-10-15', vigenteHasta: '2026-10-31', condicionesCredito: '30 días',
      archivoAdjuntoId: 'a-1', archivoNombre: 'cot.pdf',
      lineas: [{ lineaRequisicionId: partida.lineaRequisicionId, unidadCotizada: 'HOJA', factorAUnidadRq: 1, precioUnitario: 25, diasEntrega: 3 }],
    } as unknown as CotizacionComparativo;
    render(
      <CotizacionSheet abierto onClose={onClose} requisicionId="rq-1" folio="MID2026-010009" partidas={[partida]} cotizacion={cotizacion} />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText('Editar cotización')).toBeInTheDocument();
    expect(screen.getByLabelText(/Folio del proveedor/)).toHaveValue('COT-9');
    expect(screen.getByLabelText(/^Precio/)).toHaveValue(25);
    fireEvent.click(await screen.findByRole('button', { name: 'Guardar cambios' }));
    await waitFor(() => expect(mocks.editar).toHaveBeenCalledTimes(1));
    expect(mocks.editar.mock.calls[0]![0].cotizacionId).toBe('cot-1');
    expect(mocks.mutateAsync).not.toHaveBeenCalled();
    await waitFor(() => expect(onClose).toHaveBeenCalledWith({ force: true }));
  });
});
