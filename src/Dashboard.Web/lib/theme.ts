"use client";

import { createTheme } from "@mui/material/styles";

// Unchanged export: several files import this for keys, values and identifiers.
export const mono = '"SFMono-Regular", "JetBrains Mono", Menlo, Consolas, monospace';

/**
 * Teal, around 192°. Red, amber and green are already spoken for by production,
 * staging and success, and teal stays distinct from all three under the common
 * colour-vision deficiencies. Nothing else on screen is saturated for decoration —
 * saturation means risk or state.
 */
const teal = {
  300: "#6FD4DE",
  400: "#3FC1CE",
  500: "#189AAA",
  600: "#0E7490",
  700: "#0B6570",
  800: "#0A4E57",
};

const overlay = {
  light: "0 1px 2px rgba(16,24,40,.06), 0 8px 24px rgba(16,24,40,.10)",
  dark: "0 1px 2px rgba(0,0,0,.5), 0 8px 24px rgba(0,0,0,.45)",
};

const theme = createTheme({
  // The attribute here must match InitColorSchemeScript's in app/layout.tsx, or the
  // server markup and the pre-paint script disagree and the page flashes.
  cssVariables: { colorSchemeSelector: "data-color-scheme" },
  colorSchemes: {
    light: {
      palette: {
        primary: { main: teal[700], light: teal[500], dark: teal[800], contrastText: "#FFFFFF" },
        secondary: { main: "#3D4657" },
        error: { main: "#B3261E", contrastText: "#FFFFFF" },
        warning: { main: "#92500A", contrastText: "#FFFFFF" },
        success: { main: "#146C43", contrastText: "#FFFFFF" },
        info: { main: "#0B5FA5", contrastText: "#FFFFFF" },
        background: { default: "#F7F8FA", paper: "#FFFFFF" },
        divider: "#E4E7EC",
        text: { primary: "#12151A", secondary: "#5A6472", disabled: "#8A93A3" },
        action: {
          hover: "rgba(18,21,26,.04)",
          selected: "rgba(11,101,112,.08)",
          focus: "rgba(11,101,112,.12)",
        },
      },
    },
    dark: {
      palette: {
        primary: { main: teal[400], light: teal[300], dark: teal[600], contrastText: "#08171A" },
        secondary: { main: "#AAB4C4" },
        error: { main: "#FF7A7A", contrastText: "#1A0B0B" },
        warning: { main: "#E3B341", contrastText: "#1A1206" },
        success: { main: "#56D3A0", contrastText: "#04170F" },
        info: { main: "#79B8FF", contrastText: "#04121F" },
        background: { default: "#0E1116", paper: "#161A21" },
        divider: "#262B34",
        text: { primary: "#E6E9EF", secondary: "#99A2B2", disabled: "#6C7686" },
        action: {
          hover: "rgba(255,255,255,.05)",
          selected: "rgba(63,193,206,.14)",
          focus: "rgba(63,193,206,.20)",
        },
      },
    },
  },

  shape: { borderRadius: 8 },

  // 14px base. This is the density decision; the rest of the scale follows from it.
  typography: {
    fontFamily:
      'var(--font-sans), -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif',
    fontSize: 14,
    h4: { fontSize: "1.5rem", fontWeight: 600, letterSpacing: "-.015em" },
    h5: { fontSize: "1.1875rem", fontWeight: 600, letterSpacing: "-.01em" },
    h6: { fontSize: "1rem", fontWeight: 600 },
    subtitle2: { fontSize: ".8125rem", fontWeight: 600 },
    body1: { fontSize: ".875rem", lineHeight: 1.55 },
    body2: { fontSize: ".8125rem", lineHeight: 1.5 },
    caption: { fontSize: ".75rem" },
    overline: { fontSize: ".6875rem", fontWeight: 600, letterSpacing: ".08em", lineHeight: 1.8 },
    button: { textTransform: "none", fontWeight: 500 },
  },

  components: {
    MuiCssBaseline: {
      styleOverrides: (t) => ({
        "*:focus-visible": {
          outline: `2px solid ${t.vars?.palette.primary.main ?? t.palette.primary.main}`,
          outlineOffset: 2,
        },
        "@media (prefers-reduced-motion: reduce)": {
          "*, *::before, *::after": {
            animationDuration: "0.01ms !important",
            transitionDuration: "0.01ms !important",
          },
        },
      }),
    },

    // Flat by default: separation comes from a 1px border. Only overlays float.
    MuiPaper: {
      defaultProps: { elevation: 0 },
      styleOverrides: {
        root: { backgroundImage: "none" },
        elevation0: ({ theme: t }) => ({ border: `1px solid ${t.vars.palette.divider}` }),
      },
    },
    MuiMenu: {
      styleOverrides: {
        paper: ({ theme: t }) => ({
          border: `1px solid ${t.vars.palette.divider}`,
          boxShadow: overlay.light,
          [t.getColorSchemeSelector("dark")]: { boxShadow: overlay.dark },
        }),
      },
    },
    MuiDialog: {
      defaultProps: { fullWidth: true, maxWidth: "sm" },
      styleOverrides: {
        paper: ({ theme: t }) => ({
          border: `1px solid ${t.vars.palette.divider}`,
          boxShadow: overlay.light,
          [t.getColorSchemeSelector("dark")]: { boxShadow: overlay.dark },
        }),
      },
    },

    MuiButton: { defaultProps: { disableElevation: true, size: "small" } },
    MuiTextField: { defaultProps: { size: "small" } },
    MuiSelect: { defaultProps: { size: "small" } },
    MuiIconButton: { defaultProps: { size: "small" } },
    MuiTooltip: { defaultProps: { arrow: true } },
    MuiChip: { styleOverrides: { root: { fontWeight: 500 }, sizeSmall: { height: 22 } } },
    MuiTable: { defaultProps: { size: "small" } },
    MuiTableCell: {
      styleOverrides: {
        root: ({ theme: t }) => ({ borderColor: t.vars.palette.divider, padding: "6px 12px" }),
        head: ({ theme: t }) => ({
          fontWeight: 600,
          fontSize: ".75rem",
          letterSpacing: ".02em",
          color: t.vars.palette.text.secondary,
          background: t.vars.palette.background.default,
        }),
      },
    },
    MuiListItemButton: { styleOverrides: { root: { borderRadius: 6 } } },
  },
});

export default theme;
