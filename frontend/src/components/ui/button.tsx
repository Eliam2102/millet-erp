import { Slot } from '@radix-ui/react-slot';
import { cva, type VariantProps } from 'class-variance-authority';
import { type ComponentProps } from 'react';
import { cn } from '@/lib/utils';

const buttonVariants = cva(
  "inline-flex items-center justify-center gap-1.5 whitespace-nowrap rounded-md max-sm:min-h-touch text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand disabled:cursor-not-allowed disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:size-3.5 [&_svg]:stroke-[1.8] [&_svg]:shrink-0",
  {
    variants: {
      variant: {
        default: 'bg-brand text-primary-foreground hover:bg-brand-hover disabled:bg-brand-disabled disabled:opacity-100',
        destructive: 'bg-danger text-destructive-foreground hover:bg-danger-fg',
        outline: 'border border-line-control bg-surface-card text-ink hover:bg-surface-subtle',
        secondary: 'border border-line-control bg-surface-card text-ink hover:bg-surface-subtle',
        'secondary-danger': 'border border-line-control bg-surface-card text-danger-fg hover:bg-danger-bg',
        ghost: 'bg-transparent text-ink-secondary hover:bg-surface-muted',
        link: 'text-brand underline-offset-4 hover:text-brand-hover hover:underline',
      },
      size: {
        default: 'h-ctl-lg px-3.5',
        sm: 'h-ctl-sm rounded-sm px-2.5 text-xs',
        lg: 'h-ctl-xl px-3.5',
        icon: 'size-ctl-lg max-sm:min-w-touch',
      },
    },
    defaultVariants: {
      variant: 'default',
      size: 'default',
    },
  },
);

type ButtonProps = ComponentProps<'button'> &
  VariantProps<typeof buttonVariants> & {
    asChild?: boolean;
  };

export function Button({
  className,
  variant,
  size,
  asChild = false,
  ...props
}: ButtonProps) {
  const Comp = asChild ? Slot : 'button';
  return (
    <Comp
      data-slot="button"
      className={cn(buttonVariants({ variant, size, className }))}
      {...props}
    />
  );
}

export { buttonVariants };
