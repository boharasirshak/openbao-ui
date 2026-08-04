"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import {
  Alert,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
} from "@mui/material";
import { errorMessage } from "@/lib/client";

/**
 * Shared shell for the create/edit dialogs. It owns the busy flag and the error
 * banner so each caller only supplies fields and the action itself.
 */
export default function FormDialog({
  open,
  title,
  submitLabel = "Create",
  onClose,
  onSubmit,
  disabled,
  children,
}: {
  open: boolean;
  title: string;
  submitLabel?: string;
  onClose: () => void;
  onSubmit: () => void | Promise<void>;
  disabled?: boolean;
  children: ReactNode;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError("");
    setBusy(true);
    try {
      await onSubmit();
      onClose();
    } catch (submitError) {
      setError(errorMessage(submitError, "That did not work."));
    } finally {
      setBusy(false);
    }
  }

  function close() {
    if (busy) return;
    setError("");
    onClose();
  }

  return (
    <Dialog open={open} onClose={close} fullWidth maxWidth="sm">
      <form onSubmit={submit}>
        <DialogTitle>{title}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 0.5 }}>
            {error && <Alert severity="error">{error}</Alert>}
            {children}
          </Stack>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={close} disabled={busy}>
            Cancel
          </Button>
          <Button
            type="submit"
            variant="contained"
            disabled={busy || disabled}
            startIcon={busy ? <CircularProgress size={16} color="inherit" /> : null}
          >
            {submitLabel}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
