import { useState } from "react";
import { createRoot } from "react-dom/client";
import { Alert, Box, Button, Container, TextField, Typography } from "@mui/material";
import { login } from "./api/client";

function App() {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    try {
      await login(username, password);
    } catch {
      setError("Sign in failed.");
    }
  }
  return (
    <Container maxWidth="sm">
      <Box component="form" onSubmit={submit} sx={{ mt: 12, display: "grid", gap: 2 }}>
        <Typography variant="h4">OpenBao Secrets</Typography>
        {error && <Alert severity="error">{error}</Alert>}
        <TextField
          label="Username"
          autoComplete="username"
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          required
        />
        <TextField
          label="Password"
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
        <Button type="submit" variant="contained">
          Sign in
        </Button>
      </Box>
    </Container>
  );
}
createRoot(document.getElementById("root")!).render(<App />);
