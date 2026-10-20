import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Dialog, DialogContent, DialogTitle, DialogBody } from './dialog';
describe('Dialog', () => {
  it('tamaño sm', () => {
    render(
      <Dialog open>
        <DialogContent size="sm" aria-describedby={undefined}>
          <DialogTitle>Formulario</DialogTitle>
          <DialogBody>Contenido</DialogBody>
        </DialogContent>
      </Dialog>,
    );
    expect(screen.getByRole('dialog')).toHaveClass('max-w-[480px]');
    expect(screen.getByText('Contenido')).toHaveClass('overflow-y-auto', 'px-6', 'py-5');
  });
  it('tamaño md', () => {
    render(
      <Dialog open>
        <DialogContent size="md" aria-describedby={undefined}>
          <DialogTitle>Formulario</DialogTitle>
          <DialogBody>Contenido</DialogBody>
        </DialogContent>
      </Dialog>,
    );
    expect(screen.getByRole('dialog')).toHaveClass('max-w-[max(640px,50vw)]');
    expect(screen.getByText('Contenido')).toHaveClass('overflow-y-auto', 'px-6', 'py-5');
  });
  it('tamaño lg', () => {
    render(
      <Dialog open>
        <DialogContent size="lg" aria-describedby={undefined}>
          <DialogTitle>Formulario</DialogTitle>
          <DialogBody>Contenido</DialogBody>
        </DialogContent>
      </Dialog>,
    );
    expect(screen.getByRole('dialog')).toHaveClass('max-w-[max(960px,75vw)]');
    expect(screen.getByText('Contenido')).toHaveClass('overflow-y-auto', 'px-6', 'py-5');
  });
  it('tamaño por omisión', () => {
    render(
      <Dialog open>
        <DialogContent aria-describedby={undefined}>
          <DialogTitle>Formulario</DialogTitle>
          <DialogBody>Contenido</DialogBody>
        </DialogContent>
      </Dialog>,
    );
    expect(screen.getByRole('dialog')).toHaveClass('max-w-lg');
    expect(screen.getByText('Contenido')).toHaveClass('overflow-y-auto', 'px-6', 'py-5');
  });
});
