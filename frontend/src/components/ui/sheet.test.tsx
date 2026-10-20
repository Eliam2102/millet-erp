import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Sheet, SheetContent, SheetTitle, SheetBody } from './sheet';
describe('Sheet', () => {
  it('tamaño form', () => {
    render(
      <Sheet open>
        <SheetContent size="form" aria-describedby={undefined}>
          <SheetTitle>Formulario</SheetTitle>
          <SheetBody>Contenido</SheetBody>
        </SheetContent>
      </Sheet>,
    );
    expect(screen.getByRole('dialog')).toHaveClass('sm:max-w-[560px]');
    expect(screen.getByText('Contenido')).toHaveClass('overflow-y-auto', 'px-6', 'py-5');
  });
  it('tamaño wide', () => {
    render(
      <Sheet open>
        <SheetContent size="wide" aria-describedby={undefined}>
          <SheetTitle>Formulario</SheetTitle>
          <SheetBody>Contenido</SheetBody>
        </SheetContent>
      </Sheet>,
    );
    expect(screen.getByRole('dialog')).toHaveClass('sm:max-w-5xl');
    expect(screen.getByText('Contenido')).toHaveClass('overflow-y-auto', 'px-6', 'py-5');
  });
  it('tamaño legacy', () => {
    render(
      <Sheet open>
        <SheetContent size="legacy" aria-describedby={undefined}>
          <SheetTitle>Formulario</SheetTitle>
          <SheetBody>Contenido</SheetBody>
        </SheetContent>
      </Sheet>,
    );
    expect(screen.getByRole('dialog')).toHaveClass('sm:max-w-3xl');
    expect(screen.getByText('Contenido')).toHaveClass('overflow-y-auto', 'px-6', 'py-5');
  });
  it('tamaño md', () => {
    render(
      <Sheet open>
        <SheetContent size="md" aria-describedby={undefined}>
          <SheetTitle>Formulario</SheetTitle>
          <SheetBody>Contenido</SheetBody>
        </SheetContent>
      </Sheet>,
    );
    expect(screen.getByRole('dialog')).toHaveClass('max-w-[max(640px,50vw)]');
    expect(screen.getByText('Contenido')).toHaveClass('overflow-y-auto', 'px-6', 'py-5');
  });
  it('tamaño lg', () => {
    render(
      <Sheet open>
        <SheetContent size="lg" aria-describedby={undefined}>
          <SheetTitle>Formulario</SheetTitle>
          <SheetBody>Contenido</SheetBody>
        </SheetContent>
      </Sheet>,
    );
    expect(screen.getByRole('dialog')).toHaveClass('max-w-[max(960px,75vw)]');
    expect(screen.getByText('Contenido')).toHaveClass('overflow-y-auto', 'px-6', 'py-5');
  });
});
