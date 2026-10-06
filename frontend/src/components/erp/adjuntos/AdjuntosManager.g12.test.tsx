import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { AdjuntosManager, type AdjuntoItem } from './AdjuntosManager';
import { EstadoAdjunto } from './api/types';
import { ApiError } from '@/lib/api';
import type {
  TipoDocumentoSelectorItem,
  TipoDocumentoSelectorProps,
} from './TipoDocumentoSelector';

vi.mock('./TipoDocumentoSelector', () => ({
  TipoDocumentoSelector: ({
    items,
    value,
    onChange,
    ariaLabel = 'Tipo de documento',
  }: TipoDocumentoSelectorProps<TipoDocumentoSelectorItem>) => (
    <select
      aria-label={ariaLabel}
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)}
    >
      <option value="">Selecciona tipo</option>
      {items.map((i) => (
        <option key={i.id} value={i.id}>
          {i.descripcion}
        </option>
      ))}
    </select>
  ),
}));

const base: AdjuntoItem = {
  id: 'a-1',
  tipoDocumentoId: 't-csf',
  nombreArchivo: 'csf.pdf',
  contentType: 'application/pdf',
  tamanoBytes: 2048,
  fechaCarga: '2026-10-01T15:00:00Z',
  usuarioCargaId: 'u-1',
  estado: EstadoAdjunto.Vencido,
  vigenteHasta: '2026-10-31',
  hashSha256: 'abcdef0123456789'.repeat(4),
};
const tipos = [
  { id: 't-csf', clave: 'csf', descripcion: 'Constancia', vigenciaMeses: 3 },
  { id: 't-con', clave: 'contrato', descripcion: 'Contrato', vigenciaMeses: null },
];

function pintar(props: Partial<React.ComponentProps<typeof AdjuntosManager>> = {}) {
  return render(
    <AdjuntosManager
      adjuntos={[base]}
      tipos={tipos}
      onUpload={async () => {}}
      canUpload={false}
      resolverNombreUsuario={() => 'Ana Compras'}
      {...props}
    />,
    { wrapper: createQueryWrapper() },
  );
}

describe('<AdjuntosManager> — servicio genérico G1.2', () => {
  it('muestra estado con texto, vigencia resaltada, quién/cuándo y hash corto', () => {
    pintar();
    expect(screen.getByText('Vencido')).toBeInTheDocument();
    expect(screen.getByText(/Vigente hasta 31\/10\/2026/)).toHaveClass('text-danger-fg');
    expect(screen.getByText(/Ana Compras/)).toBeInTheDocument();
    expect(screen.getByText('abcdef01')).toHaveAttribute(
      'title',
      expect.stringContaining('SHA-256'),
    );
  });

  it('pide vigencia solo para tipos con vigenciaMeses y la envía', async () => {
    const onUpload = vi.fn(async () => {});
    const { container } = pintar({ canUpload: true, adjuntos: [], onUpload });
    const input = container.querySelector('input[type=file]') as HTMLInputElement;
    fireEvent.change(input, {
      target: { files: [new File(['%PDF'], 'a.pdf', { type: 'application/pdf' })] },
    });
    const sel = await screen.findByRole('combobox', { name: /tipo de documento/i });
    fireEvent.change(sel, { target: { value: 't-con' } });
    expect(screen.queryByLabelText(/vigente hasta/i)).not.toBeInTheDocument();
    fireEvent.change(sel, { target: { value: 't-csf' } });
    fireEvent.change(screen.getByLabelText(/vigente hasta/i), {
      target: { value: '2027-01-15' },
    });
    fireEvent.click(screen.getByRole('button', { name: /^subir$/i }));
    await waitFor(() =>
      expect(onUpload).toHaveBeenCalledWith(
        expect.objectContaining({ tipoDocumentoId: 't-csf', vigenteHasta: '2027-01-15' }),
      ),
    );
  });

  it('modoBaja: exige motivo de 5+ caracteres y llama onDarDeBaja', async () => {
    const onDarDeBaja = vi.fn(async () => {});
    pintar({ modoBaja: true, canRemove: true, onDarDeBaja });
    fireEvent.click(screen.getByRole('button', { name: /dar de baja csf\.pdf/i }));
    const dialog = await screen.findByRole('dialog');
    const confirmar = within(dialog).getByRole('button', { name: /^dar de baja$/i });
    expect(confirmar).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText(/motivo de la baja/i), {
      target: { value: 'abc' },
    });
    expect(confirmar).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText(/motivo de la baja/i), {
      target: { value: 'Documento equivocado' },
    });
    fireEvent.click(confirmar);
    await waitFor(() => expect(onDarDeBaja).toHaveBeenCalledWith('a-1', 'Documento equivocado'));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('modoBaja: el 422 de segunda baja se muestra en el diálogo abierto', async () => {
    const onDarDeBaja = vi.fn(async () => {
      throw new ApiError(
        { type: 'x', title: 't', status: 422, code: 'ADJUNTO_YA_DADO_DE_BAJA' },
        422,
      );
    });
    pintar({ modoBaja: true, canRemove: true, onDarDeBaja });
    fireEvent.click(screen.getByRole('button', { name: /dar de baja csf\.pdf/i }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText(/motivo de la baja/i), {
      target: { value: 'Motivo válido' },
    });
    fireEvent.click(within(dialog).getByRole('button', { name: /^dar de baja$/i }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Este documento ya fue dado de baja.',
    );
  });

  it('sin modoBaja no ofrece baja; toggle de bajas solo con permiso', () => {
    const { rerender } = pintar({ canRemove: true, onDarDeBaja: async () => {} });
    expect(screen.queryByRole('button', { name: /dar de baja/i })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/dados de baja/i)).not.toBeInTheDocument();
    const onVerBajasChange = vi.fn();
    rerender(
      <AdjuntosManager
        adjuntos={[base]}
        tipos={tipos}
        onUpload={async () => {}}
        canUpload={false}
        puedeVerBajas
        onVerBajasChange={onVerBajasChange}
      />,
    );
    fireEvent.click(screen.getByLabelText(/ver documentos dados de baja/i));
    expect(onVerBajasChange).toHaveBeenCalledWith(true);
  });

  it('un adjunto dado de baja muestra motivo y no ofrece descarga ni baja', () => {
    pintar({
      adjuntos: [{ ...base, estado: EstadoAdjunto.Baja, bajaEn: '2026-10-02T10:00:00Z', bajaMotivo: 'Duplicado' }],
      modoBaja: true,
      canRemove: true,
      onDarDeBaja: async () => {},
      onDescargar: async () => {},
    });
    expect(screen.getByText('Dado de baja')).toBeInTheDocument();
    expect(screen.getByText(/Duplicado/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /descargar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /dar de baja csf/i })).not.toBeInTheDocument();
  });

  it('descarga por enlace: un 403 se traduce a mensaje claro', async () => {
    const onDescargar = vi.fn(async () => {
      throw new ApiError({ type: 'x', title: 'Forbidden', status: 403 }, 403);
    });
    pintar({ onDescargar });
    fireEvent.click(screen.getByRole('button', { name: /descargar csf\.pdf/i }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/no tienes permiso/i);
  });

  it('estados de carga y error de la lista', () => {
    const { rerender } = pintar({ adjuntosLoading: true });
    expect(screen.getByRole('status')).toHaveTextContent(/cargando/i);
    rerender(
      <AdjuntosManager
        adjuntos={[]}
        tipos={tipos}
        onUpload={async () => {}}
        canUpload={false}
        adjuntosError="No se pudo cargar."
      />,
    );
    expect(screen.getByRole('alert')).toHaveTextContent('No se pudo cargar.');
  });
});
