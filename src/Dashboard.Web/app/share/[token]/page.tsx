"use client";

import { useParams } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import {
  Alert,
  Button,
  CircularProgress,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableRow,
  Typography,
} from "@mui/material";
import LockIcon from "@mui/icons-material/LockOutlined";
import VisibilityIcon from "@mui/icons-material/VisibilityOutlined";
import VisibilityOffIcon from "@mui/icons-material/VisibilityOffOutlined";
import { CopyButton, MaskedValue } from "@/components/SecretValue";
import { openShare } from "@/lib/client";
import { mono } from "@/lib/theme";

type State =
  | { status: "idle" }
  | { status: "opening" }
  | { status: "open"; values: Record<string, string> }
  | { status: "gone" };

export default function SharePage() {
  const token = String(useParams<{ token: string }>().token);
  const [state, setState] = useState<State>({ status: "idle" });
  const [revealed, setRevealed] = useState(false);

  return (
    <Stack alignItems="center" justifyContent="center" sx={{ minHeight: "100vh", px: 2 }}>
      <Paper sx={{ p: 4, width: "100%", maxWidth: 560 }}>
        <Stack spacing={1} alignItems="center" sx={{ mb: 3 }}>
          <LockIcon sx={{ fontSize: 34, color: "primary.main" }} />
          <Typography variant="h5">Someone shared a secret with you</Typography>
          <Typography color="text.secondary" variant="body2" align="center">
            This link works once. Opening it destroys it, so copy what you need before you close the
            page.
          </Typography>
        </Stack>

        {state.status === "idle" && (
          <Button
            variant="contained"
            size="large"
            fullWidth
            onClick={() => {
              setState({ status: "opening" });
              void openShare(token).then((values) =>
                setState(values ? { status: "open", values } : { status: "gone" }),
              );
            }}
          >
            Reveal once
          </Button>
        )}

        {state.status === "opening" && (
          <Stack direction="row" spacing={1.5} alignItems="center" justifyContent="center">
            <CircularProgress size={18} />
            <Typography color="text.secondary">Opening…</Typography>
          </Stack>
        )}

        {state.status === "gone" && (
          <Alert severity="error">
            This link has already been used, has expired, or never existed.
          </Alert>
        )}

        {state.status === "open" && (
          <>
            <Alert severity="warning" sx={{ mb: 2 }}>
              The link is now spent. Refreshing this page will not show it again.
            </Alert>
            <Stack direction="row" justifyContent="flex-end" sx={{ mb: 1 }}>
              <Button
                size="small"
                startIcon={revealed ? <VisibilityOffIcon /> : <VisibilityIcon />}
                onClick={() => setRevealed(!revealed)}
              >
                {revealed ? "Hide" : "Reveal"}
              </Button>
            </Stack>
            <Paper variant="outlined">
              <Table size="small">
                <TableBody>
                  {Object.entries(state.values).map(([key, value]) => (
                    <TableRow key={key}>
                      <TableCell sx={{ fontFamily: mono, fontSize: 13, width: "40%" }}>
                        {key}
                      </TableCell>
                      <TableCell>
                        <Stack direction="row" spacing={1} alignItems="center">
                          <MaskedValue value={value} revealed={revealed} />
                          <CopyButton value={value} title={`Copy ${key}`} />
                        </Stack>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Paper>
          </>
        )}
      </Paper>

      <Typography
        variant="caption"
        color="text.disabled"
        sx={{ mt: 3, maxWidth: 480 }}
        align="center"
      >
        The value was held by OpenBao behind a single-use token and was never stored by this
        application.
      </Typography>
      <ClearOnLeave active={state.status === "open"} />
    </Stack>
  );
}

/**
 * Belt and braces: wipe the revealed value from memory when the tab is hidden or
 * closed, so it is not sitting in a background tab all afternoon.
 */
function ClearOnLeave({ active }: { active: boolean }) {
  const reloaded = useRef(false);
  useEffect(() => {
    if (!active) return;
    const onHide = () => {
      if (document.visibilityState === "hidden" && !reloaded.current) {
        reloaded.current = true;
        location.replace(location.pathname);
      }
    };
    document.addEventListener("visibilitychange", onHide);
    return () => document.removeEventListener("visibilitychange", onHide);
  }, [active]);
  return null;
}
