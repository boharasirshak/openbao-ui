"use client";

import { IconButton, Tooltip } from "@mui/material";
import { useColorScheme } from "@mui/material/styles";
import LightIcon from "@mui/icons-material/LightModeOutlined";
import DarkIcon from "@mui/icons-material/DarkModeOutlined";
import SystemIcon from "@mui/icons-material/ComputerOutlined";

const NEXT = { system: "light", light: "dark", dark: "system" } as const;
const LABEL = { system: "Match system", light: "Light", dark: "Dark" } as const;

export default function ThemeModeToggle() {
  const { mode, setMode } = useColorScheme();

  // The theme is already correct before hydration, but the *icon* depends on the
  // stored preference, which the server cannot know. MUI reports that by leaving
  // mode undefined until mounted, so hold the space rather than render a wrong icon.
  if (!mode) return <IconButton disabled sx={{ visibility: "hidden" }} />;

  return (
    <Tooltip title={`Theme: ${LABEL[mode]}`}>
      <IconButton onClick={() => setMode(NEXT[mode])} aria-label={`Theme: ${LABEL[mode]}`}>
        {mode === "light" ? (
          <LightIcon fontSize="small" />
        ) : mode === "dark" ? (
          <DarkIcon fontSize="small" />
        ) : (
          <SystemIcon fontSize="small" />
        )}
      </IconButton>
    </Tooltip>
  );
}
