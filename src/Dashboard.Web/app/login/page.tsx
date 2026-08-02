"use client";

import { useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Box,
  Button,
  Paper,
  Stack,
  TextField,
  Typography,
  CircularProgress,
} from "@mui/material";
import LockIcon from "@mui/icons-material/LockOutlined";
import { errorMessage, login } from "@/lib/client";

export default function LoginPage() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError("");
    setBusy(true);
    try {
      queryClient.setQueryData(["session"], await login(username, password));
      router.replace("/projects");
    } catch (loginError) {
      setError(errorMessage(loginError, "Sign in failed."));
      setBusy(false);
    }
  }

  return (
    <Stack alignItems="center" justifyContent="center" sx={{ minHeight: "100vh", px: 2 }}>
      <Paper sx={{ p: 4, width: "100%", maxWidth: 400 }}>
        <Stack spacing={1} alignItems="center" sx={{ mb: 3 }}>
          <LockIcon sx={{ fontSize: 34, color: "primary.main" }} />
          <Typography variant="h5">OpenBao Secrets</Typography>
          <Typography color="text.secondary" variant="body2">
            Sign in with your OpenBao username
          </Typography>
        </Stack>

        <Box component="form" onSubmit={submit} sx={{ display: "grid", gap: 2 }}>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField
            label="Username"
            autoComplete="username"
            value={username}
            onChange={(event) => setUsername(event.target.value)}
            autoFocus
            required
            fullWidth
          />
          <TextField
            label="Password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
            fullWidth
          />
          <Button
            type="submit"
            variant="contained"
            size="large"
            disabled={busy}
            startIcon={busy ? <CircularProgress size={16} color="inherit" /> : null}
          >
            {busy ? "Signing in…" : "Sign in"}
          </Button>
        </Box>
      </Paper>
      <Typography variant="caption" color="text.disabled" sx={{ mt: 3 }}>
        Secrets are never stored in your browser. Sessions expire automatically.
      </Typography>
    </Stack>
  );
}
