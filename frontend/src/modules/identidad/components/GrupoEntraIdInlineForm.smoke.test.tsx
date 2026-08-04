import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { GrupoEntraIdInlineForm } from '@/modules/identidad/components/GrupoEntraIdInlineForm';

describe('<GrupoEntraIdInlineForm> — smoke', () => {
  it('renderiza el form con border dashed primary y label "Asociar grupo"', () => {
    const { container } = render(
      <GrupoEntraIdInlineForm rolId="r-1" onCancel={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      screen.getByRole('button', { name: /asociar grupo/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('form', { name: /asociar grupo entra id/i }),
    ).toBeInTheDocument();
    const form = container.querySelector('form');
    expect(form?.className).toMatch(/border-dashed/);
    expect(form?.className).not.toMatch(/border-amber/);
  });

  it('botón Cancelar llama onCancel', () => {
    const onCancel = vi.fn();
    render(
      <GrupoEntraIdInlineForm rolId="r-1" onCancel={onCancel} />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByRole('button', { name: /cancelar/i }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('Esc llama onCancel cuando no está pending', () => {
    const onCancel = vi.fn();
    render(
      <GrupoEntraIdInlineForm rolId="r-1" onCancel={onCancel} />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.keyDown(screen.getByRole('form'), { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('los inputs Object ID y Nombre del grupo están presentes', () => {
    render(
      <GrupoEntraIdInlineForm rolId="r-1" onCancel={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      screen.getByPlaceholderText(
        '00000000-0000-0000-0000-000000000000',
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByPlaceholderText('Compras - Aprobadores'),
    ).toBeInTheDocument();
  });
});
