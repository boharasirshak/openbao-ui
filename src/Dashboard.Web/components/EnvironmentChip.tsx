"use client";

import { Chip, type ChipProps } from "@mui/material";

/** Production is red on purpose, so a risky environment never looks routine. */
function tone(environment: string): ChipProps["color"] {
  if (environment === "production") return "error";
  if (environment === "staging") return "warning";
  return "default";
}

export default function EnvironmentChip({
  environment,
  ...props
}: { environment: string } & ChipProps) {
  return <Chip size="small" label={environment} color={tone(environment)} {...props} />;
}
