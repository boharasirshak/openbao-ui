"use client";

import { createTheme, alpha } from "@mui/material/styles";

const surface = "#15181f";
const border = "#262b36";

const theme = createTheme({
  palette: {
    mode: "dark",
    background: { default: "#0e1015", paper: surface },
    primary: { main: "#e9c46a", contrastText: "#14161c" },
    secondary: { main: "#7aa2f7" },
    error: { main: "#f07178" },
    warning: { main: "#e0af68" },
    success: { main: "#7fd1a4" },
    divider: border,
    text: { primary: "#e6e8ee", secondary: "#98a0b3" },
  },
  shape: { borderRadius: 8 },
  typography: {
    fontFamily:
      '"Inter", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif',
    h5: { fontSize: "1.25rem", fontWeight: 600 },
    h6: { fontSize: "1rem", fontWeight: 600 },
    button: { textTransform: "none", fontWeight: 500 },
  },
  components: {
    MuiPaper: {
      styleOverrides: {
        root: { backgroundImage: "none", border: `1px solid ${border}` },
      },
    },
    MuiButton: { defaultProps: { disableElevation: true } },
    MuiTextField: { defaultProps: { size: "small" } },
    MuiSelect: { defaultProps: { size: "small" } },
    MuiTableCell: {
      styleOverrides: {
        root: { borderColor: border },
        head: { fontWeight: 600, color: "#98a0b3", background: alpha("#ffffff", 0.02) },
      },
    },
    MuiTooltip: { defaultProps: { arrow: true } },
    // Monospace everywhere a secret key, value or identifier is shown.
    MuiChip: { styleOverrides: { root: { fontWeight: 500 } } },
  },
});

export const mono = '"SFMono-Regular", "JetBrains Mono", Menlo, Consolas, monospace';

export default theme;
