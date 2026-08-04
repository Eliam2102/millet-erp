import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useUnsavedChangesGuard } from '@/lib/hooks/useUnsavedChangesGuard';

describe('useUnsavedChangesGuard', () => {
  let addSpy: ReturnType<typeof vi.spyOn>;
  let removeSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    addSpy = vi.spyOn(window, 'addEventListener');
    removeSpy = vi.spyOn(window, 'removeEventListener');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('NO instala listener cuando isDirty=false', () => {
    renderHook(({ dirty }) => useUnsavedChangesGuard(dirty), {
      initialProps: { dirty: false },
    });
    const beforeunloadAdds = addSpy.mock.calls.filter(
      ([evt]) => evt === 'beforeunload',
    );
    expect(beforeunloadAdds).toHaveLength(0);
  });

  it('instala listener cuando isDirty=true', () => {
    renderHook(({ dirty }) => useUnsavedChangesGuard(dirty), {
      initialProps: { dirty: true },
    });
    const beforeunloadAdds = addSpy.mock.calls.filter(
      ([evt]) => evt === 'beforeunload',
    );
    expect(beforeunloadAdds).toHaveLength(1);
  });

  it('desinstala listener cuando isDirty pasa de true a false', () => {
    const { rerender } = renderHook(
      ({ dirty }) => useUnsavedChangesGuard(dirty),
      { initialProps: { dirty: true } },
    );

    rerender({ dirty: false });

    const beforeunloadRemoves = removeSpy.mock.calls.filter(
      ([evt]) => evt === 'beforeunload',
    );
    expect(beforeunloadRemoves.length).toBeGreaterThanOrEqual(1);
  });

  it('desinstala listener al unmount', () => {
    const { unmount } = renderHook(() => useUnsavedChangesGuard(true));
    const removesAntes = removeSpy.mock.calls.filter(
      ([e]) => e === 'beforeunload',
    ).length;

    unmount();

    const removesDespues = removeSpy.mock.calls.filter(
      ([e]) => e === 'beforeunload',
    ).length;
    expect(removesDespues).toBeGreaterThan(removesAntes);
  });

  it('el handler invoca preventDefault y setea returnValue', () => {
    renderHook(() => useUnsavedChangesGuard(true));

    const beforeunloadAdds = addSpy.mock.calls.filter(
      ([evt]) => evt === 'beforeunload',
    );
    const handler = beforeunloadAdds[0][1] as (e: BeforeUnloadEvent) => void;

    const fakeEvent = {
      preventDefault: vi.fn(),
      returnValue: '',
    } as unknown as BeforeUnloadEvent;

    handler(fakeEvent);

    expect(fakeEvent.preventDefault).toHaveBeenCalled();
    expect(fakeEvent.returnValue).toBe('');
  });
});
