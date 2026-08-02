"use client";

import { useState, type FormEvent } from "react";
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
  TextField,
  Typography,
} from "@mui/material";
import VisibilityIcon from "@mui/icons-material/VisibilityOutlined";
import VisibilityOffIcon from "@mui/icons-material/VisibilityOffOutlined";
import { PageHeader } from "@/components/AppShell";
import { CopyButton, MaskedValue } from "@/components/SecretValue";
import { mono } from "@/lib/theme";
import { errorMessage, getDatabaseCredential, type DatabaseCredentialResponse } from "@/lib/client";

export default function DatabasePage() {
  const [role, setRole] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [credential, setCredential] = useState<DatabaseCredentialResponse | null>(null);
  const [revealed, setRevealed] = useState(false);

  async function request(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError("");
    setCredential(null);
    setRevealed(false);
    try {
      setCredential(await getDatabaseCredential(role.trim()));
    } catch (requestError) {
      setError(errorMessage(requestError, "No credential was issued for that role."));
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <PageHeader
        title="Database credentials"
        description="Ask OpenBao for a short-lived database login. It expires on its own, so there is nothing to clean up."
      />

      <Stack spacing={3} sx={{ maxWidth: 720 }}>
        <Paper sx={{ p: 3 }}>
          <Stack component="form" onSubmit={request} direction="row" spacing={2}>
            <TextField
              label="Database role"
              value={role}
              onChange={(event) => setRole(event.target.value)}
              helperText="The role name configured in the OpenBao database engine."
              fullWidth
              autoFocus
              required
            />
            <Button
              type="submit"
              variant="contained"
              disabled={busy || !role.trim()}
              sx={{ height: 40 }}
              startIcon={busy ? <CircularProgress size={16} color="inherit" /> : null}
            >
              {busy ? "Requesting…" : "Request"}
            </Button>
          </Stack>
        </Paper>

        {error && <Alert severity="error">{error}</Alert>}

        {credential && (
          <Paper sx={{ overflow: "hidden" }}>
            <Alert severity="warning" sx={{ borderRadius: 0 }}>
              Copy these now. Leaving this page discards them, and the lease expires on its own.
            </Alert>
            <Table size="small">
              <TableBody>
                <Row label="Username">
                  <Typography sx={{ fontFamily: mono, fontSize: 13 }}>
                    {credential.username}
                  </Typography>
                  <CopyButton value={credential.username} title="Copy username" />
                </Row>
                <Row label="Password">
                  <MaskedValue value={credential.password} revealed={revealed} />
                  <Button
                    size="small"
                    startIcon={revealed ? <VisibilityOffIcon /> : <VisibilityIcon />}
                    onClick={() => setRevealed(!revealed)}
                  >
                    {revealed ? "Hide" : "Reveal"}
                  </Button>
                  <CopyButton value={credential.password} title="Copy password" />
                </Row>
                <Row label="Lease ID">
                  <Typography
                    sx={{
                      fontFamily: mono,
                      fontSize: 12,
                      color: "text.secondary",
                      wordBreak: "break-all",
                    }}
                  >
                    {credential.leaseId}
                  </Typography>
                  <CopyButton value={credential.leaseId} title="Copy lease ID" />
                </Row>
                <Row label="Expires">
                  <Typography variant="body2">
                    {new Date(credential.expiresAt).toLocaleString()}
                  </Typography>
                </Row>
              </TableBody>
            </Table>
          </Paper>
        )}
      </Stack>
    </>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <TableRow>
      <TableCell width={130} sx={{ color: "text.secondary" }}>
        {label}
      </TableCell>
      <TableCell>
        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
          {children}
        </Stack>
      </TableCell>
    </TableRow>
  );
}
