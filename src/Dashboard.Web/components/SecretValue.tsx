"use client";

import { useState } from "react";
import { Box, IconButton, Tooltip } from "@mui/material";
import CopyIcon from "@mui/icons-material/ContentCopyOutlined";
import CheckIcon from "@mui/icons-material/CheckOutlined";
import { mono } from "@/lib/theme";

/** Masked until revealed. The dot count is fixed so the length is not a hint. */
export function MaskedValue({ value, revealed }: { value: string; revealed: boolean }) {
  return (
    <Box
      component="span"
      sx={{
        fontFamily: mono,
        fontSize: 13,
        wordBreak: "break-all",
        color: revealed ? "text.primary" : "text.secondary",
        userSelect: revealed ? "text" : "none",
      }}
    >
      {revealed ? value || <em>(empty)</em> : "••••••••••••"}
    </Box>
  );
}

export function CopyButton({ value, title = "Copy" }: { value: string; title?: string }) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard access needs a secure context; nothing useful to do if it is denied.
    }
  }

  return (
    <Tooltip title={copied ? "Copied" : title}>
      <IconButton size="small" onClick={copy}>
        {copied ? <CheckIcon fontSize="small" color="success" /> : <CopyIcon fontSize="small" />}
      </IconButton>
    </Tooltip>
  );
}
