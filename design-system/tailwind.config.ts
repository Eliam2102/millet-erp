// Millet ERP — Tailwind config (React + Tailwind + shadcn/ui)
// Requiere tokens.css importado en el entry. Usar SOLO estas clases de color/tamaño;
// no usar la paleta default de Tailwind (blue-600, gray-500, etc.).
import type { Config } from "tailwindcss";

export default {
  darkMode: ["class"],
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      fontFamily: {
        sans: ["IBM Plex Sans", "system-ui", "sans-serif"],
        mono: ["IBM Plex Mono", "ui-monospace", "monospace"],
      },
      fontSize: {
        // [size, lineHeight]
        "2xs": ["11px", "14px"],
        xs: ["12px", "16px"],
        sm: ["13px", "18px"], // base
        md: ["14px", "20px"],
        lg: ["15px", "20px"],
        xl: ["16px", "22px"],
        "2xl": ["18px", "24px"],
        "3xl": ["22px", "28px"],
        "4xl": ["24px", "30px"],
      },
      colors: {
        // shadcn
        border: "hsl(var(--border))",
        input: "hsl(var(--input))",
        ring: "hsl(var(--ring))",
        background: "hsl(var(--background))",
        foreground: "hsl(var(--foreground))",
        primary: { DEFAULT: "hsl(var(--primary))", foreground: "hsl(var(--primary-foreground))" },
        secondary: { DEFAULT: "hsl(var(--secondary))", foreground: "hsl(var(--secondary-foreground))" },
        destructive: { DEFAULT: "hsl(var(--destructive))", foreground: "hsl(var(--destructive-foreground))" },
        muted: { DEFAULT: "hsl(var(--muted))", foreground: "hsl(var(--muted-foreground))" },
        accent: { DEFAULT: "hsl(var(--accent))", foreground: "hsl(var(--accent-foreground))" },
        popover: { DEFAULT: "hsl(var(--popover))", foreground: "hsl(var(--popover-foreground))" },
        card: { DEFAULT: "hsl(var(--card))", foreground: "hsl(var(--card-foreground))" },
        // Millet
        surface: {
          page: "var(--mt-surface-page)",
          card: "var(--mt-surface-card)",
          subtle: "var(--mt-surface-subtle)",
          muted: "var(--mt-surface-muted)",
          selected: "var(--mt-surface-selected)",
          "row-focus": "var(--mt-surface-row-focus)",
        },
        ink: {
          DEFAULT: "var(--mt-text-primary)",
          secondary: "var(--mt-text-secondary)",
          strong: "var(--mt-text-strong-ui)",
          muted: "var(--mt-text-muted)",
          subtle: "var(--mt-text-subtle)",
        },
        line: {
          DEFAULT: "var(--mt-border)",
          divider: "var(--mt-border-divider)",
          row: "var(--mt-border-row)",
          control: "var(--mt-border-control)",
          dashed: "var(--mt-border-dashed)",
        },
        brand: {
          DEFAULT: "var(--mt-primary)",
          hover: "var(--mt-primary-hover)",
          soft: "var(--mt-primary-soft)",
          "soft-border": "var(--mt-primary-soft-border)",
          disabled: "var(--mt-primary-disabled)",
          avatar: "var(--mt-avatar-bg)",
        },
        rail: {
          DEFAULT: "var(--mt-rail-bg)",
          active: "var(--mt-rail-active-bg)",
          fg: "var(--mt-rail-fg)",
          disabled: "var(--mt-rail-disabled-fg)",
        },
        success: { DEFAULT: "var(--mt-success)", bg: "var(--mt-success-bg)", fg: "var(--mt-success-fg)" },
        warning: {
          DEFAULT: "var(--mt-warning)", bg: "var(--mt-warning-bg)", fg: "var(--mt-warning-fg)",
          "note-bg": "var(--mt-warning-note-bg)", "note-fg": "var(--mt-warning-note-fg)",
        },
        danger: { DEFAULT: "var(--mt-danger)", bg: "var(--mt-danger-bg)", fg: "var(--mt-danger-fg)", ring: "var(--mt-danger-ring)" },
        info: { DEFAULT: "var(--mt-info)", bg: "var(--mt-info-bg)", fg: "var(--mt-info-fg)" },
        neutral: { bg: "var(--mt-neutral-bg)", fg: "var(--mt-neutral-fg)" },
        notify: "var(--mt-notification-dot)",
      },
      borderRadius: {
        xs: "4px",
        sm: "6px",
        md: "8px",
        lg: "10px",
        xl: "12px",
        "2xl": "14px",
      },
      boxShadow: {
        card: "var(--mt-shadow-card)",
        "card-flat": "var(--mt-shadow-card-flat)",
        dialog: "var(--mt-shadow-dialog)",
        sheet: "var(--mt-shadow-sheet)",
        "tab-active": "inset 0 -2px 0 var(--mt-primary)",
        "list-selected": "inset 3px 0 0 var(--mt-primary)",
      },
      spacing: {
        rail: "76px",
        "rail-item": "64px",
        "module-panel": "240px",
        topbar: "56px",
        "master-list": "320px",
        sheet: "560px",
        dialog: "640px",
      },
      height: {
        "ctl-xs": "22px",
        "ctl-sm": "28px",
        "ctl-md": "30px",
        "ctl-lg": "34px",
        "ctl-xl": "36px",
        touch: "44px",
      },
    },
  },
  plugins: [require("tailwindcss-animate")],
} satisfies Config;
