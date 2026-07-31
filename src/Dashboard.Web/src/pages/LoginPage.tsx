import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Alert, Box, Button, Container, TextField, Typography } from "@mui/material";
import { useNavigate } from "react-router-dom";
import { login } from "../api/client";

export function LoginPage() {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError("");
    try {
      const session = await login(username, password);
      queryClient.setQueryData(["session"], session);
      navigate("/projects", { replace: true });
    } catch {
      setError("Sign in failed.");
    }
  }

  return (
    <Container maxWidth="sm">
      <Box component="form" onSubmit={submit} sx={{ mt: 12, display: "grid", gap: 2 }}>
        <Typography variant="h4">OpenBao Secrets</Typography>
        <Typography color="text.secondary">Secure internal developer access</Typography>
        {error && <Alert severity="error">{error}</Alert>}
        <TextField
          label="Username"
          autoComplete="username"
          value={username}
          onChange={(event) => setUsername(event.target.value)}
          required
        />
        <TextField
          label="Password"
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          required
        />
        <Button type="submit" variant="contained">
          Sign in
        </Button>
      </Box>
    </Container>
  );
}
